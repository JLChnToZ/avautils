using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using JLChnToZ.CommonUtils.Dynamic;

namespace JLChnToZ.EditorExtensions {
    using static MecanimUtils;

    public class HumanoidAvatarBuilderWindow : EditorWindow {
        static readonly Vector3Int NullMuscleIndeces = new Vector3Int(-1, -1, -1);
        static GUIContent tempContent;
        static bool[] requireBones;
        static int[] parentBoneIndeces;
        static Vector3Int[] muscleIndeces;
        static HumanLimit[] defaultHumanLimits;
        dynamic boneRenderer;
        Animator selectedAnimator;
        HumanDescription humanDescription;
        Transform[] boneMap;
        HumanLimit[] humanLimits;
        Vector2 scrollPos;
        bool showAdvanced;

        static void Init() {
            if (muscleIndeces == null) {
                muscleIndeces = new Vector3Int[HumanTrait.MuscleCount];
                for (int i = 0; i < muscleIndeces.Length; i++)
                    muscleIndeces[i] = new Vector3Int(
                        HumanTrait.MuscleFromBone(i, 0),
                        HumanTrait.MuscleFromBone(i, 1),
                        HumanTrait.MuscleFromBone(i, 2)
                    );
            }
            if (defaultHumanLimits == null) {
                defaultHumanLimits = new HumanLimit[HumanTrait.BoneCount];
                for (int i = 0; i < defaultHumanLimits.Length; i++) {
                    Vector3 min = Vector3.zero, max = Vector3.zero;
                    for (int x = 0; x < 3; x++) {
                        int muscleId = muscleIndeces[i][x];
                        min[x] = HumanTrait.GetMuscleDefaultMin(muscleId);
                        max[x] = HumanTrait.GetMuscleDefaultMax(muscleId);
                    }
                    defaultHumanLimits[i] = new HumanLimit {
                        useDefaultValues = true,
                        min = min,
                        max = max,
                    };
                }
            }
            if (tempContent == null) tempContent = new GUIContent();
        }

        static int CompareDepth((Transform, int d) a, (Transform, int d) b) => a.d - b.d;

        [MenuItem("Tools/JLChnToZ/Humanoid Avatar Builder")]
        static void ShowWindow() => GetWindow<HumanoidAvatarBuilderWindow>().Show();

        [MenuItem("CONTEXT/Animator/(Re-)Build Humanoid Avatar")]
        static void ShowWindowContext(MenuCommand command) {
            var animator = command.context as Animator;
            if (animator == null) return;
            var window = GetWindow<HumanoidAvatarBuilderWindow>();
            window.Show();
            window.selectedAnimator = animator;
            window.GuessBones(true);
        }

        void OnEnable() {
            Init();
            titleContent = new GUIContent(EditorGUIUtility.IconContent("Avatar Icon")) {
                text = "Humanoid Avatar Builder",
            };
            if (boneRenderer == null) boneRenderer = Limitless.Construct("UnityEditor.Handles+BoneRenderer, UnityEditor");
            if (selectedAnimator == null) {
                var selectedAnimators = Selection.GetFiltered<Animator>(SelectionMode.Editable | SelectionMode.ExcludePrefab);
                if (selectedAnimators.Length > 0) selectedAnimator = selectedAnimators[0];
            }
            ResetAll();
            SceneView.duringSceneGui += OnSceneGUI;
        }

        void OnDisable() {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        void ResetAll() {
            boneMap = new Transform[HumanTrait.BoneCount];
            humanLimits = new HumanLimit[HumanTrait.BoneCount];
            Array.Copy(defaultHumanLimits, humanLimits, humanLimits.Length);
            humanDescription = new HumanDescription {
                armStretch = 0.05F,
                feetSpacing = 0,
                legStretch = 0.05F,
                lowerArmTwist = 0.5f,
                lowerLegTwist = 0.5f,
                upperArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                hasTranslationDoF = false,
            };
            GuessBones(true);
        }

        void OnGUI() {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                using (var changed = new EditorGUI.ChangeCheckScope()) {
                    selectedAnimator = EditorGUILayout.ObjectField(selectedAnimator, typeof(Animator), true) as Animator;
                    if (changed.changed) ResetAll();
                }
                if (GUILayout.Button("Auto Fill", EditorStyles.toolbarButton)) GuessBones(false);
                if (GUILayout.Button("Copy Existing Config", EditorStyles.toolbarButton)) CopyExistingConfigurations();
                if (GUILayout.Button("Clear & Reset", EditorStyles.toolbarButton)) ResetAll();
                if (GUILayout.Button("Build", EditorStyles.toolbarButton)) Build();
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.HelpBox("Make sure your humanoid skeleton is in T-Pose before build.\n(Or you will have to explicitly set the bone rotation offsets)", MessageType.Info);
            using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPos)) {
                scrollPos = scroll.scrollPosition;
                if (showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Additional Settings", true))
                    using (new EditorGUI.IndentLevelScope()) {
                        humanDescription.upperArmTwist = EditorGUILayout.Slider("Upper Arm Twist", humanDescription.upperArmTwist, 0, 1);
                        humanDescription.lowerArmTwist = EditorGUILayout.Slider("Lower Arm Twist", humanDescription.lowerArmTwist, 0, 1);
                        humanDescription.upperLegTwist = EditorGUILayout.Slider("Upper Leg Twist", humanDescription.upperLegTwist, 0, 1);
                        humanDescription.lowerLegTwist = EditorGUILayout.Slider("Lower Leg Twist", humanDescription.lowerLegTwist, 0, 1);
                        humanDescription.armStretch = EditorGUILayout.Slider("Arm Stretch", humanDescription.armStretch, 0, 1);
                        humanDescription.legStretch = EditorGUILayout.Slider("Leg Stretch", humanDescription.legStretch, 0, 1);
                        humanDescription.feetSpacing = EditorGUILayout.Slider("Feet Spacing", humanDescription.feetSpacing, 0, 1);
                        humanDescription.hasTranslationDoF = EditorGUILayout.Toggle("Translation DoF", humanDescription.hasTranslationDoF);
                    }
                var humanBoneNames = HumanBoneNames;
                var muscleNames = MuscleNames;
                for (int i = 0; i < boneMap.Length; i++) {
                    bool shouldOverride = false;
                    using (new EditorGUILayout.HorizontalScope()) {
                        boneMap[i] = EditorGUILayout.ObjectField(humanBoneNames[i], boneMap[i], typeof(Transform), true) as Transform;
                        bool hasMuscles = muscleIndeces[i] == NullMuscleIndeces;
                        using (new EditorGUI.DisabledScope(hasMuscles))
                            shouldOverride = GUILayout.Toggle(!humanLimits[i].useDefaultValues, "Override Limits", GUILayout.ExpandWidth(false));
                        humanLimits[i].useDefaultValues = hasMuscles || !shouldOverride;
                    }
                    if (shouldOverride)
                        using (new EditorGUI.IndentLevelScope()) {
                            Vector3 minValues = humanLimits[i].min, maxValues = humanLimits[i].max, restValues = humanLimits[i].center;
                            for (int x = 0; x < 3; x++) {
                                using (new EditorGUILayout.HorizontalScope()) {
                                    int index = muscleIndeces[i][x];
                                    if (index < 0) continue;
                                    float prefixSize = EditorGUIUtility.labelWidth;
                                    EditorGUIUtility.labelWidth = prefixSize - 12;
                                    float min = minValues[x], max = maxValues[x];
                                    EditorGUILayout.PrefixLabel(muscleNames[index]);
                                    min = EditorGUILayout.FloatField(min, GUILayout.Width(50));
                                    EditorGUILayout.MinMaxSlider(ref min, ref max, -180F, 180F);
                                    max = EditorGUILayout.FloatField(max, GUILayout.Width(50));
                                    EditorGUIUtility.labelWidth = prefixSize * 0.5F;
                                    restValues[x] = EditorGUILayout.Slider("Ref. Angle", restValues[x], min, max);
                                    minValues[x] = min;
                                    maxValues[x] = max;
                                    EditorGUIUtility.labelWidth = prefixSize;
                                }
                            }
                            humanLimits[i].axisLength = EditorGUILayout.FloatField("Axis Length", humanLimits[i].axisLength);
                            humanLimits[i].center = restValues;
                            humanLimits[i].min = minValues;
                            humanLimits[i].max = maxValues;
                        }
                    if (boneMap[i] == null) {
                        if (requireBones[i]) EditorGUILayout.HelpBox($"Bone \"{humanBoneNames[i]}\" is required.", MessageType.Error);
                        continue;
                    }
                    if (selectedAnimator != null && !boneMap[i].IsChildOf(selectedAnimator.transform)) {
                        EditorGUILayout.HelpBox($"Bone \"{humanBoneNames[i]}\" must be inside of \"{selectedAnimator.name}\" in hierarchy.", MessageType.Error);
                        continue;
                    }
                    var hasCorrectParent = true;
                    var parentBone = boneMap[i].parent;
                    int boneIndex = i;
                    while (true) {
                        boneIndex = parentBoneIndeces[boneIndex];
                        if (boneIndex < 0) break;
                        hasCorrectParent = parentBone == boneMap[boneIndex];
                        if (hasCorrectParent || requireBones[boneIndex]) break;
                    }
                    if (!hasCorrectParent) {
                        EditorGUILayout.HelpBox($"Bone \"{humanBoneNames[i]}\" must be child of \"{humanBoneNames[parentBoneIndeces[i]]}\".", MessageType.Error);
                        continue;
                    }
                }
            }
        }

        void OnSceneGUI(SceneView view) {
            if (!view.drawGizmos) return;
            var boneColor = new Color(0, 0.8F, 0.3F, 0.5F);
            boneRenderer.ClearInstances();
            for (int i = 0; i < boneMap.Length; i++) {
                var bone = boneMap[i];
                if (bone == null) continue;
                var parent = bone.parent;
                if (i > 0 && parent != null)
                    boneRenderer.AddBoneInstance(
                        parent.position,
                        bone.position,
                        boneColor
                    );
                boneRenderer.AddBoneLeafInstance(
                    bone.position,
                    bone.rotation,
                    (parent != null ? Vector3.Distance(parent.position, bone.position) : 1) * 0.4F,
                    boneColor
                );
            }
            boneRenderer.Render();
        }

        void CopyExistingConfigurations() {
            var avatar = selectedAnimator.avatar;
            if (avatar == null || !avatar.isValid) {
                ResetAll();
                return;
            }
            GuessBones(true);
            humanDescription = avatar.humanDescription;
            var humanBoneNames = HumanBoneNames;
            foreach (var human in humanDescription.human) {
                int boneIndex = Array.IndexOf(humanBoneNames, human.humanName);
                if (boneIndex < 0) continue;
                humanLimits[boneIndex] = human.limit;
            }
            humanDescription.skeleton = null;
            humanDescription.human = null;
        }

        void GuessBones(bool replaceAll = false) {
            if (selectedAnimator == null) return;
            var newBoneMap = GuessHumanoidBodyBones(selectedAnimator.transform);
            if (replaceAll) {
                boneMap = newBoneMap;
                return;
            }
            for (int i = 0; i < boneMap.Length; i++)
                if (boneMap[i] == null)
                    boneMap[i] = newBoneMap[i];
        }

        void Build() {
            if (selectedAnimator == null) return;
            var ambiguousNameMap = new Dictionary<Transform, string>();
            var boneTransforms = new Dictionary<Transform, (Vector3 position, Quaternion rotation)>();
            var humanBoneNames = HumanBoneNames;
            try {
                var root = selectedAnimator.transform;
                var boneNames = new HashSet<string>();
                var skeletonBoneTransforms = new HashSet<Transform>();
                var transformsToAdd = new List<(Transform, int)>();
                {
                    // Reset all parent bone transform if any,
                    // This is to prevent the avatar from being built with incorrect bone position and rotation.
                    var firstBone = boneMap[(int)HumanBodyBones.Hips];
                    var firstBonePosition = firstBone.position;
                    var firstBoneRotation = firstBone.rotation;
                    for (var parent = firstBone.parent; parent != null && parent != root; parent = parent.parent) {
                        if (parent.localPosition == Vector3.zero && parent.localRotation == Quaternion.identity) continue;
                        Undo.RecordObject(parent, "Reset Bone Transform");
#if UNITY_2021_3_OR_NEWER
                        parent.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
#else
                        parent.localPosition = Vector3.zero;
                        parent.localRotation = Quaternion.identity;
#endif
                    }
                    if (firstBone.position != firstBonePosition || firstBone.rotation != firstBoneRotation) {
                        Undo.RecordObject(firstBone, "Reset Bone Transform");
                        firstBone.SetPositionAndRotation(firstBonePosition, firstBoneRotation);
                    }
                }
                foreach (var bone in boneMap)
                    for (var parent = bone; parent != null && parent != root; parent = parent.parent) {
                        if (!skeletonBoneTransforms.Add(parent)) break;
                        boneNames.Add(parent.name);
                    }
                var stack = new Stack<(int, Transform)>();
                stack.Push((0, root));
                while (stack.Count > 0) {
                    var (index, bone) = stack.Pop();
                    if (index >= bone.childCount) continue;
                    var child = bone.GetChild(index);
                    stack.Push((index + 1, bone));
                    stack.Push((0, child));
                    if (skeletonBoneTransforms.Contains(child))
                        transformsToAdd.Add((child, stack.Count));
                    else if (boneNames.Contains(child.name)) {
                        // Ambigunous names will cause error when building avatar.
                        Debug.LogWarning($"[Humanoid Avatar Builder] Transform name \"{child.name}\" is ambiguous, this may prevents built humanoid avatar working. You will have to fix it afterward.", child);
                        ambiguousNameMap[child] = child.name;
                        var temp = new string[boneNames.Count];
                        boneNames.CopyTo(temp);
                        var tempName = ObjectNames.GetUniqueName(temp, child.name);
                        boneNames.Add(tempName);
                        child.name = tempName;
                    }
                    // Record bone transform, as building avatar will change their values.
                    boneTransforms[bone] = (bone.localPosition, bone.localRotation);
                }
                var skeletonBones = new SkeletonBone[transformsToAdd.Count];
                var humanBones = new List<HumanBone>(boneMap.Length);
                transformsToAdd.Sort(CompareDepth);
                int i = 0;
                foreach (var (bone, _) in transformsToAdd) {
                    var d_skeletonBone = Limitless.Wrap(new SkeletonBone {
                        name = bone.name,
                        position = bone.localPosition,
                        rotation = bone.localRotation,
                        scale = bone.localScale,
                    });
                    var parent = bone.parent;
                    if (parent != null && parent != root)
                        d_skeletonBone.parentName = parent.name;
                    skeletonBones[i++] = d_skeletonBone;
                }
                for (i = 0; i < boneMap.Length; i++) {
                    var bone = boneMap[i];
                    if (bone == null) continue;
                    humanBones.Add(new HumanBone {
                        boneName = bone.name,
                        humanName = humanBoneNames[i],
                        limit = humanLimits[i],
                    });
                }
                humanDescription.skeleton = skeletonBones;
                humanDescription.human = humanBones.ToArray();
                var d_humanDescription = Limitless.Wrap(humanDescription);
                d_humanDescription.m_SkeletonHasParents = true;
                d_humanDescription.m_HasExtraRoot = boneMap[0].parent != root;
                humanDescription = d_humanDescription;
                var orgAvatar = selectedAnimator.avatar;
                if (orgAvatar != null) selectedAnimator.avatar = null; // Temporarily unset the avatar as it will affect the building process.
                var avatar = AvatarBuilder.BuildHumanAvatar(root.gameObject, humanDescription);
                if (orgAvatar != null) selectedAnimator.avatar = orgAvatar;
                if (avatar == null || !avatar.isValid) {
                    Debug.LogError("[Humanoid Avatar Builder] Failed to Build humanoid avatar.", root);
                    return;
                }
                string savePath = null;
                if (orgAvatar != null) savePath = AssetDatabase.GetAssetPath(orgAvatar);
                if (string.IsNullOrEmpty(savePath)) savePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(selectedAnimator.gameObject);
                if (string.IsNullOrEmpty(savePath)) savePath = "Assets";
                else savePath = Path.GetDirectoryName(savePath);
                savePath = EditorUtility.SaveFilePanelInProject("Save Avatar", avatar.name, "asset", "Save built humanoid avatar to", savePath);
                if (!string.IsNullOrEmpty(savePath)) AssetDatabase.CreateAsset(avatar, savePath);
                Undo.RecordObject(selectedAnimator, "Set Avatar");
                selectedAnimator.avatar = avatar;
            } finally {
                foreach (var kv in ambiguousNameMap) kv.Key.name = kv.Value;
                // Restore bone transform.
                foreach (var kv in boneTransforms) {
                    var (position, rotation) = kv.Value;
#if UNITY_2021_3_OR_NEWER
                    kv.Key.SetLocalPositionAndRotation(position, rotation);
#else
                    kv.Key.localPosition = position;
                    kv.Key.localRotation = rotation;
#endif
                }
            }
        }
    }
}
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditorInternal;
using JLChnToZ.CommonUtils.Dynamic;

namespace JLChnToZ.EditorExtensions {
    public class BoneEditorWindow : EditorWindow {
        const string BASE_MENU_PATH = "CONTEXT/" + nameof(SkinnedMeshRenderer) + "/";
        const string MENU_PATH = BASE_MENU_PATH + "Edit Bone References";
        const string COPY_MENU_PATH = BASE_MENU_PATH + "Copy Bone References";
        const string PASTE_MENU_PATH = BASE_MENU_PATH + "Paste Bone References";
        const string EDITOR_PREFS_AUTOAPPLY = "BoneEditorWindow.AutoApply";
        public static Color unReferencedBoneColor = new Color(0, 0, 0, 0);
        public static Color noCoverageBoneColor = new Color(0, 0, 0.5F, 0);
        static GUIContent tempContent, warningIcon, infoIcon, filterIcon, plusIcon, avatarIcon, rootIcon, notAssignedIcon, noWeightIcon;
        static readonly GUILayoutOption[] noExpandWidth = new[] { GUILayout.ExpandWidth(false) };
        static readonly GUILayoutOption[] forceExpandWidth = new[] { GUILayout.ExpandWidth(true) };
        static readonly HashSet<Transform> boneSet = new HashSet<Transform>();
        static readonly Stack<Transform> boneChain = new Stack<Transform>();
        static readonly HashSet<Transform> walkedBones = new HashSet<Transform>();
        static readonly HashSet<string> walkedNames = new HashSet<string>();
        static readonly Dictionary<Transform, float> boneCoverageMap = new Dictionary<Transform, float>();
        static readonly HashSet<(Transform, Transform)> boneGizmoMap = new HashSet<(Transform, Transform)>();
        static readonly Dictionary<Transform, bool> actualBones = new Dictionary<Transform, bool>();
        static readonly HashSet<Transform> rootBones = new HashSet<Transform>();
        static bool autoApply;
        static Transform[] copiedBones;
        static dynamic boneRenderer;
        static Action refreshBoneCoverageMap;
        HashSet<Transform> humanoidBones;
        SkinnedMeshRenderer target;
        Mesh mesh;
        Transform[] bones;
        float totalCoverage;
        static float maxCoverage;
        static bool shouldRefreshBoneCoverageMap;
        static Transform selectedBone;
        BoneCoverage[] boneInfos;
        PoseModeState poseModeState;
        ReorderableList list;
        Vector2 scrollPos;

        static BoneEditorWindow() {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        [MenuItem(MENU_PATH)]
        static void BoneEditorMenu(MenuCommand command) {
            if (!BoneEditorMenuEnabled(command)) return;
            var instance = CreateInstance<BoneEditorWindow>();
            instance.target = command.context as SkinnedMeshRenderer;
            instance.ShowUtility();
            instance.OnEnable();
        }

        [MenuItem(COPY_MENU_PATH)]
        static void CopyBones(MenuCommand command) {
            if (!BoneEditorMenuEnabled(command)) return;
            copiedBones = (command.context as SkinnedMeshRenderer).bones;
        }

        [MenuItem(PASTE_MENU_PATH)]
        static void PasteBones(MenuCommand command) {
            if (!PasteBonesEnabled(command)) return;
            var smr = command.context as SkinnedMeshRenderer;
            Undo.RecordObject(smr, "Paste Bones");
            smr.bones = copiedBones;
        }

        [MenuItem(MENU_PATH, true)]
        [MenuItem(COPY_MENU_PATH, true)]
        static bool BoneEditorMenuEnabled(MenuCommand command) {
            var smr = command.context as SkinnedMeshRenderer;
            if (smr == null) return false;
            var mesh = smr.sharedMesh;
            if (mesh == null || !mesh.HasVertexAttribute(VertexAttribute.BlendIndices)) return false;
            return true;
        }

        [MenuItem(PASTE_MENU_PATH, true)]
        static bool PasteBonesEnabled(MenuCommand command) =>
            BoneEditorMenuEnabled(command) &&
            copiedBones != null &&
            copiedBones.Length == (command.context as SkinnedMeshRenderer).sharedMesh.bindposes.Length;

        void OnEnable() {
            if (filterIcon == null) filterIcon = EditorGUIUtility.IconContent("d_Animation.FilterBySelection");
            if (plusIcon == null) plusIcon = EditorGUIUtility.IconContent("Toolbar Plus");
            if (filterIcon == null) filterIcon = EditorGUIUtility.IconContent("Animation.FilterBySelection");
            if (boneRenderer == null) boneRenderer = Limitless.Construct("UnityEditor.Handles+BoneRenderer, UnityEditor");
            if (target == null) return;
            minSize = new Vector2(500, 100);
            mesh = target.sharedMesh;
            RefreshBones();
            Undo.undoRedoPerformed += OnUndoRedo;
            autoApply = EditorPrefs.GetBool(EDITOR_PREFS_AUTOAPPLY, true);
            refreshBoneCoverageMap += RefreshBoneCoverageMap;
        }

        void OnLostFocus() {
            selectedBone = null;
        }

        void OnDisable() {
            if (poseModeState != null) {
                poseModeState.Dispose();
                poseModeState = null;
            }
            Undo.undoRedoPerformed -= OnUndoRedo;
            refreshBoneCoverageMap -= RefreshBoneCoverageMap;
            shouldRefreshBoneCoverageMap = true;
        }

        void UpdateDirty(bool isDirty) {
            titleContent = new GUIContent($"Bone Editor{(isDirty ? "*" : "")}");
        }

        void OnGUI() {
            if (target == null) {
                Close();
                return;
            }
            if (list == null || mesh != target.sharedMesh) {
                mesh = target.sharedMesh;
                if (mesh == null || !mesh.HasVertexAttribute(VertexAttribute.BlendIndices)) {
                    Close();
                    return;
                }
                RefreshBones();
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                if (GUILayout.Button(GetGUIContent("Clear Empty", "Clear empty bone references."), EditorStyles.toolbarButton, noExpandWidth))
                    ClearEmpty();
                if (GUILayout.Button(GetGUIContent("Fill Empty", "Fill empty bone references."), EditorStyles.toolbarButton, noExpandWidth))
                    FillEmpty();
                if (GUILayout.Button(GetGUIContent("Set Bindpose", "Move all bones to bindpose."), EditorStyles.toolbarButton, noExpandWidth))
                    SetBindpose();
                if (GUILayout.Button(GetGUIContent("Reassign Dup.", "Reassign duplicated bones."), EditorStyles.toolbarButton, noExpandWidth))
                    ReassignDuplicatedBones();
                if (GUILayout.Button(GetGUIContent("Rename Dup.", "Rename duplicated bones."), EditorStyles.toolbarButton, noExpandWidth))
                    RenameDuplicatedBones();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, noExpandWidth))
                    RefreshBones();
                if (poseModeState == null) {
                    if (GUILayout.Toggle(false, "Repose", EditorStyles.toolbarButton, noExpandWidth))
                        EnablePoseMode();
                    using (var changed = new EditorGUI.ChangeCheckScope()) {
                        autoApply = GUILayout.Toggle(autoApply, GetGUIContent(autoApply ? "Auto Apply" : "Auto", "Auto apply changes to target skinned mesh renderer."), EditorStyles.toolbarButton, noExpandWidth);
                        if (changed.changed) EditorPrefs.SetBool(EDITOR_PREFS_AUTOAPPLY, autoApply);
                    }
                    if (!autoApply && GUILayout.Button("Apply", EditorStyles.toolbarButton, noExpandWidth))
                        ApplyBones();
                } else {
                    if (!GUILayout.Toggle(true, "Repose", EditorStyles.toolbarButton, noExpandWidth))
                        CancelPoseMode();
                    if (GUILayout.Button("Apply", EditorStyles.toolbarButton, noExpandWidth))
                        ApplyPoseMode();
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(true)) {
                EditorGUILayout.ObjectField(mesh, typeof(Mesh), false, GUILayout.Width(EditorGUIUtility.labelWidth + ReorderableList.Defaults.dragHandleWidth - 4));
                EditorGUILayout.ObjectField(target, typeof(SkinnedMeshRenderer), true, forceExpandWidth);
            }
            using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPos)) {
                scrollPos = scroll.scrollPosition;
                walkedBones.Clear();
                walkedNames.Clear();
                list.DoLayoutList();
            }
            if (focusedWindow == this) selectedBone = list.index >= 0 ? bones[list.index] : null;
        }

        void OnHierarchyChange() {
            if (target == null) {
                Close();
                return;
            }
            var root = target.rootBone;
            if (root == null) root = target.transform;
            if (humanoidBones != null) humanoidBones.Clear();
            var animator = root.GetComponentInParent<Animator>();
            if (animator == null) return;
            var avatar = animator.avatar;
            if (avatar == null || !avatar.isValid || !avatar.isHuman) return;
            if (humanoidBones == null) humanoidBones = new HashSet<Transform>();
            for (var i = HumanBodyBones.Hips; i < HumanBodyBones.LastBone; i++) {
                var bone = animator.GetBoneTransform(i);
                if (bone != null) humanoidBones.Add(bone);
            }
        }

        static void OnSceneGUI(SceneView sceneView) {
            if (Event.current.type != EventType.Repaint || !sceneView.drawGizmos) return;
            RefreshBoneCoverageMapIfNeeded();
            if (boneRenderer == null) return;
            boneRenderer.ClearInstances();
            foreach (var (current, child) in boneGizmoMap) {
                var position = current.position;
                if (child != null) {
                    boneRenderer.AddBoneInstance(position, child.position, GetBoneColor(child, current));
                    continue;
                }
                var parent = current.parent;
                boneRenderer.AddBoneLeafInstance(
                    position, current.rotation,
                    (parent != null ? Vector3.Distance(parent.position, position) : 1) * 0.4F,
                    GetBoneColor(current)
                );
            }
            boneRenderer.Render();
            foreach (var rootBone in rootBones) {
                var position = rootBone.position;
                var size = HandleUtility.GetHandleSize(position) * 0.5F;
                Handles.color = Color.red;
                Handles.DrawLine(position, position + rootBone.right * size);
                Handles.color = Color.green;
                Handles.DrawLine(position, position + rootBone.up * size);
                Handles.color = Color.blue;
                Handles.DrawLine(position, position + rootBone.forward * size);
            }
        }

        static void RefreshBoneCoverageMapIfNeeded() {
            if (!shouldRefreshBoneCoverageMap) return;
            shouldRefreshBoneCoverageMap = false;
            boneCoverageMap.Clear();
            boneGizmoMap.Clear();
            actualBones.Clear();
            rootBones.Clear();
            maxCoverage = 0;
            refreshBoneCoverageMap?.Invoke();
        }

        void RefreshBoneCoverageMap() {
            boneSet.Clear();
            boneSet.UnionWith(bones);
            boneChain.Clear();
            if (target != null) {
                var root = target.rootBone;
                if (root == null) root = target.transform;
                rootBones.Add(root);
            }
            for (int i = 0; i < boneInfos.Length; i++) {
                var bone = bones[i];
                if (bone == null) continue;
                boneCoverageMap.TryGetValue(bone, out float coverage);
                coverage += boneInfos[i].coverage;
                boneCoverageMap[bone] = coverage;
                actualBones[bone] = false;
                maxCoverage = Mathf.Max(maxCoverage, coverage);
                Transform firstValidChild = null;
                int childCount = bone.childCount;
                for (int j = 0; j < childCount; j++) {
                    var child = bone.GetChild(j);
                    if (boneSet.Contains(child)) {
                        firstValidChild = child;
                        break;
                    }
                }
                if (firstValidChild == null && childCount > 0) firstValidChild = bone.GetChild(0);
                boneGizmoMap.Add((bone, firstValidChild));
                for (var parent = bone.parent; parent != null; parent = parent.parent) {
                    boneChain.Push(parent);
                    if (!boneSet.Contains(parent)) continue;
                    var current = boneChain.Pop();
                    while (boneChain.Count > 0) {
                        var child = boneChain.Pop();
                        var boneAdded = !boneGizmoMap.Add((current, child));
                        current = child;
                        if (boneAdded) {
                            current = bone.parent;
                            break;
                        }
                    }
                    boneGizmoMap.Add((current, bone));
                    break;
                }
                boneChain.Clear();
            }
        }

        void OnUndoRedo() {
            if (autoApply) {
                RefreshBones();
                Repaint();
            }
        }

        void RefreshBones() {
            if (target == null) return;
            bones = target.bones;
            int boneCount = mesh.bindposes.Length;
            if (bones == null || bones.Length != boneCount) {
                var newBones = new Transform[boneCount];
                if (bones != null) Array.Copy(bones, newBones, Mathf.Min(bones.Length, boneCount));
                bones = newBones;
                ApplyBones("Initialize Bones");
            } else
                UpdateDirty(false);
            boneInfos = new BoneCoverage[boneCount];
            totalCoverage = 0;
            foreach (var boneWeight in mesh.GetAllBoneWeights()) {
                totalCoverage += boneWeight.weight;
                boneInfos[boneWeight.boneIndex] += boneWeight.weight;
            }
            list = new ReorderableList(bones, typeof(Transform), true, true, false, false) {
                drawElementCallback = OnListDrawElement,
                drawHeaderCallback = DoNotDraw,
                drawFooterCallback = DoNotDraw,
                onReorderCallback = OnListReorder,
                elementHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing,
                showDefaultBackground = false,
                headerHeight = 0,
                footerHeight = 0,
            };
            OnHierarchyChange();
            shouldRefreshBoneCoverageMap = true;
        }

        void OnListDrawElement(Rect rect, int index, bool isActive, bool isFocused) {
            var bone = bones[index];
            ref var boneInfo = ref boneInfos[index];
            rect.y += EditorGUIUtility.standardVerticalSpacing;
            var rect2 = rect;
            rect2.width -= EditorGUIUtility.singleLineHeight * 2;
            rect2.height = EditorGUIUtility.singleLineHeight;
            var contentColor = GUI.contentColor;
            if (boneInfo.coverage == 0) {
                var newContentColor = contentColor;
                newContentColor.a *= 0.5F;
                GUI.contentColor = newContentColor;
            }
            var iconRect = rect2;
            iconRect.x = rect2.xMin + EditorGUIUtility.labelWidth;
            iconRect.width = 16;
            rect2 = EditorGUI.PrefixLabel(rect2, GetGUIContent($"{index} ({boneInfo.refCount}, {boneInfo.coverage / totalCoverage:0.##%})"));
            if (boneInfo.coverage <= 0) {
                if (noWeightIcon == null)
                    noWeightIcon = new GUIContent(EditorGUIUtility.IconContent("UnLinked")) {
                        tooltip = "This bone has no weights. In most cases you can safely remove this bone reference.",
                    };
                iconRect.x -= iconRect.width;
                GUI.Label(iconRect, noWeightIcon);
            } else if (bone == null) {
                if (notAssignedIcon == null)
                    notAssignedIcon = new GUIContent(EditorGUIUtility.IconContent("Invalid")) {
                        tooltip = "This bone is not assigned and it has weights. Parts of the mesh binded to this bone will breaks.",
                    };
                iconRect.x -= iconRect.width;
                GUI.Label(iconRect, notAssignedIcon);
            }
            using (var changed = new EditorGUI.ChangeCheckScope()) {
                bone = EditorGUI.ObjectField(rect2, bone, typeof(Transform), true) as Transform;
                if (changed.changed) {
                    ReplaceBone(index, bone);
                    ApplyBones("Replace Bone Transform");
                }
            }
            iconRect = rect2;
            iconRect.x = rect2.xMax - EditorGUIUtility.singleLineHeight - 4;
            iconRect.width = EditorGUIUtility.singleLineHeight;
            if (bone != null) {
                if (humanoidBones != null && humanoidBones.Contains(bone)) {
                    if (avatarIcon == null)
                        avatarIcon = new GUIContent(EditorGUIUtility.IconContent("Avatar Icon")) {
                            tooltip = "This bone is a humanoid bone.",
                        };
                    iconRect.x -= iconRect.width;
                    GUI.Label(iconRect, avatarIcon);
                }
                if (target.rootBone == bone) {
                    if (rootIcon == null)
                        rootIcon = new GUIContent(EditorGUIUtility.IconContent("AvatarPivot")) {
                            tooltip = "This bone is the root bone.",
                        };
                    iconRect.x -= iconRect.width;
                    GUI.Label(iconRect, rootIcon);
                }
            }
            GUI.contentColor = contentColor;
            rect2.xMin = rect2.xMax + 2;
            rect2.width = EditorGUIUtility.singleLineHeight;
#if UNITY_2021_2_OR_NEWER
            var iconButtonStyle = EditorStyles.iconButton;
#else
            var iconButtonStyle = EditorStyles.label;
#endif
            if (GUI.Button(
                rect2,
                bone == null ?
                GetGUIContent(tooltip: "Create new bone transform at bindpose.", iconContent: plusIcon) :
                GetGUIContent(tooltip: "Try move bone transform to bindpose.", iconContent: filterIcon),
                iconButtonStyle
            )) {
                AutoSetBone(index, true);
                ApplyBones(bone == null ? "Create Bone Transform" : "Move Bone Transform", true);
            }
            rect2.x += 16;
            if (bone != null) {
                bool isDuplicateName = !walkedNames.Add(bone.name);
                bool isDuplicateBone = !walkedBones.Add(bone);
                var rootBone = target.rootBone;
                if (rootBone == null) rootBone = target.transform;
                if (!bone.IsChildOf(rootBone))
                    EditorGUI.LabelField(rect2, GetWarningContent(tooltip: "This bone is not under root bone. This is not recommend unless it is intentional."), EditorStyles.label);
                else if (isDuplicateBone) {
                    if (GUI.Button(rect2, GetInfoContent(tooltip: "This bone is duplicated. You may need to reassign a new bone in some cases."), iconButtonStyle) &&
                        EditorUtility.DisplayDialog("Reassign Bone", $"Reassign bone \"{bone.name}\"?", "Yes", "No")) {
                        ReassignBone(index);
                        ApplyBones("Replace Bone Transform");
                    }
                } else if (isDuplicateName) {
                    if (GUI.Button(rect2, GetInfoContent(tooltip: "This bone has the same name with another bone. You may need to rename it in some cases."), iconButtonStyle)) {
                        var newName = GetUniqueNameForBone(bone);
                        if (EditorUtility.DisplayDialog("Rename Bone", $"Rename bone \"{bone.name}\" to \"{newName}\"? This may break some animation clips.", "Rename", "Cancel")) {
                            Undo.RecordObject(bone.gameObject, "Rename Bone");
                            bone.name = newName;
                        }
                    }
                }
            }
        }

        static void DoNotDraw(Rect rect) { }

        void OnListReorder(ReorderableList list) => ApplyBones("Reorder Bones");

        bool AutoSetBone(int index, bool move) {
            var bone = bones[index];
            var rootBone = target.rootBone;
            if (rootBone == null) rootBone = target.transform;
            var bindposes = mesh.bindposes;
            GameObject go = null;
            Matrix4x4 offset;
            if (bone == null) {
                go = new GameObject($"{target.name}.Bone {index}");
                bone = go.transform;
                bone.SetParent(rootBone, false);
                go.name = GetUniqueNameForBone(bone, rootBone);
                ReplaceBone(index, bone);
                offset = rootBone.localToWorldMatrix;
            } else if (!move) return false;
            else if (bone == rootBone) {
                var parentBone = bone.parent;
                if (parentBone == null) return false;
                offset = parentBone.localToWorldMatrix;
            } else {
                RefreshBoneCoverageMapIfNeeded();
                var parentBone = bone.parent;
                while (parentBone != null) {
                    if (boneCoverageMap.ContainsKey(parentBone)) break;
                    parentBone = parentBone.parent;
                }
                if (parentBone == null) offset = rootBone.localToWorldMatrix;
                else {
                    int parentIndex = Array.LastIndexOf(bones, parentBone, index);
                    if (parentIndex < 0) parentIndex = Array.IndexOf(bones, parentBone, index);
                    offset = parentBone.localToWorldMatrix * bindposes[parentIndex];
                }
            }
            offset *= bindposes[index].inverse;
            if (go == null) Undo.RecordObject(bone, "Move Bone Transform To Bindpose");
            bone.SetPositionAndRotation(offset.MultiplyPoint(Vector3.zero), offset.rotation);
            var scale = offset.lossyScale;
            var boneParent = bone.parent;
            if (boneParent != null) {
                var boneParentScale = boneParent.lossyScale;
                scale.x /= boneParentScale.x;
                scale.y /= boneParentScale.y;
                scale.z /= boneParentScale.z;
            }
            bone.localScale = scale;
            if (go != null) Undo.RegisterCreatedObjectUndo(go, "Create Bone");
            return true;
        }

        void ApplyBones(string customUndoName = null, bool isUndoGroup = false) {
            if (poseModeState != null) return;
            bool forced = string.IsNullOrEmpty(customUndoName);
            if (autoApply || forced) {
                using (var so = new SerializedObject(target)) {
                    so.Update();
                    var bonesProperty = so.FindProperty("m_Bones");
                    bonesProperty.arraySize = bones.Length;
                    for (int i = 0; i < bones.Length; i++) {
                        var boneProperty = bonesProperty.GetArrayElementAtIndex(i);
                        boneProperty.objectReferenceValue = bones[i];
                    }
                    so.ApplyModifiedProperties();
                }
                UpdateDirty(false);
            } else UpdateDirty(true);
            shouldRefreshBoneCoverageMap = true;
            if (isUndoGroup) {
                if (!forced) Undo.SetCurrentGroupName(customUndoName);
                Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
            }
        }

        void ReplaceBone(int index, Transform bone) {
            bones[index] = bone;
            shouldRefreshBoneCoverageMap = true;
        }

        void ClearEmpty() {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null && boneInfos[i].coverage <= 0) ReplaceBone(i, null);
            ApplyBones("Clear Empty Bones", true);
        }

        void FillEmpty() {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] == null && boneInfos[i].coverage > 0) AutoSetBone(i, false);
            ApplyBones("Fill Empty Bones", true);
        }

        void SetBindpose() {
            walkedBones.Clear();
            foreach (var i in GetSortedBoneIndecesByDepth())
                if (bones[i] != null && !walkedBones.Add(bones[i])) AutoSetBone(i, true);
            ApplyBones("Move Bones To Bindpose", true);
        }

        void ReassignDuplicatedBones() {
            walkedBones.Clear();
            foreach (var i in GetSortedBoneIndecesByDepth())
                if (bones[i] != null && !walkedBones.Add(bones[i])) ReassignBone(i);
            ApplyBones("Reassign Duplicated Bones", true);
        }

        void RenameDuplicatedBones() {
            walkedNames.Clear();
            walkedBones.Clear();
            foreach (var i in GetSortedBoneIndecesByDepth()) {
                var bone = bones[i];
                if (bone == null || walkedBones.Add(bone)) continue;
                var name = bone.name;
                if (walkedNames.Add(name)) continue;
                name = GetUniqueNameForBone(bone);
                walkedNames.Add(name);
                Undo.RecordObject(bone.gameObject, "Rename Bone");
                bone.name = name;
            }
            ApplyBones("Rename Duplicated Bones", true);
        }

        void EnablePoseMode() {
            if (poseModeState != null) return;
            poseModeState = new PoseModeState(target, bones);
        }

        void ApplyPoseMode() {
            if (poseModeState == null) return;
            var mesh = poseModeState.TargetMesh;
            Undo.RecordObject(target, "Apply Pose");
            try {
                if (!poseModeState.Apply()) return;
                var newMesh = poseModeState.TargetMesh;
                var path = EditorUtility.SaveFilePanelInProject(
                    "Save Modified Mesh",
                    $"{target.name} Baked Mesh",
                    "asset",
                    "Save modified mesh to project folder."
                );
                if (string.IsNullOrEmpty(path)) {
                    DestroyImmediate(newMesh);
                    return;
                }
                AssetDatabase.CreateAsset(newMesh, path);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                mesh = newMesh;
            } finally {
                target.sharedMesh = mesh;
                target.bones = bones;
                poseModeState.Dispose();
                poseModeState = null;
            }
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        }

        void CancelPoseMode() {
            if (poseModeState == null) return;
            poseModeState.Dispose();
            poseModeState = null;
        }

        int[] GetSortedBoneIndecesByDepth() {
            var depthMap = new Dictionary<Transform, int>();
            foreach (var bone in bones) {
                if (bone == null) continue;
                var depth = 0;
                var parent = bone.parent;
                while (parent != null) {
                    if (depthMap.TryGetValue(parent, out var parentDepth)) {
                        depth = parentDepth + 1;
                        break;
                    }
                    parent = parent.parent;
                    depth++;
                }
                depthMap[bone] = depth;
            }
            var boneWithIndex = new (Transform t, int i)[bones.Length];
            for (int i = 0; i < bones.Length; i++) boneWithIndex[i] = (bones[i], i);
            Array.Sort(boneWithIndex, (l, r) => {
                if (l.t == null || !depthMap.TryGetValue(l.t, out var lDepth)) lDepth = -1;
                if (r.t == null || !depthMap.TryGetValue(r.t, out var rDepth)) rDepth = -1;
                return lDepth != rDepth ? lDepth - rDepth : l.i - r.i;
            });
            return Array.ConvertAll(boneWithIndex, x => x.i);
        }

        static Color GetBoneColor(Transform bone, Transform selection = null) {
            bool hasReference = boneCoverageMap.TryGetValue(bone, out float coverage);
            if (selection == null) selection = bone;
            else if (hasReference && boneCoverageMap.TryGetValue(selection, out var coverage2))
                coverage = (coverage + coverage2) * 0.5F;
            Color result;
            if (selectedBone == selection)
                result = Handles.selectedColor;
            else if (coverage > 0) {
                coverage = Mathf.Sqrt(coverage / maxCoverage);
                result = Color.HSVToRGB(
                    Mathf.Lerp(0.6666667F, 0.3333333F, coverage),
                    Mathf.Lerp(2F, 0F, coverage),
                    Mathf.Lerp(0.5F, 1F, coverage)
                );
                result.a = 0.5F;
            } else result = hasReference ? noCoverageBoneColor : unReferencedBoneColor;
            return result;
        }

        static GUIContent GetGUIContent(string title = null, string tooltip = null, GUIContent iconContent = null) {
            if (tempContent == null) tempContent = new GUIContent();
            tempContent.text = title ?? string.Empty;
            tempContent.tooltip = tooltip ?? string.Empty;
            tempContent.image = iconContent?.image;
            return tempContent;
        }

        static GUIContent GetWarningContent(string title = null, string tooltip = null) {
            if (warningIcon == null) warningIcon = EditorGUIUtility.IconContent("console.warnicon.sml");
            return GetGUIContent(title, tooltip, warningIcon);
        }

        static GUIContent GetInfoContent(string title = null, string tooltip = null) {
            if (infoIcon == null) infoIcon = EditorGUIUtility.IconContent("console.infoicon.sml");
            return GetGUIContent(title, tooltip, infoIcon);
        }

        string GetUniqueNameForBone(Transform bone, Transform parent = null) {
            var newName = bone.name;
            if (parent == null) parent = bone.parent;
            if (parent != null) newName = GameObjectUtility.GetUniqueNameForSibling(parent, newName);
            var existNames = new HashSet<string>();
            foreach (var otherBone in bones)
                if (otherBone != null && otherBone != bone)
                    existNames.Add(otherBone.name);
            if (existNames.Contains(newName)) {
                var existNameArray = new string[existNames.Count];
                existNames.CopyTo(existNameArray);
                newName = ObjectNames.GetUniqueName(existNameArray, newName);
            }
            return newName;
        }

        void ReassignBone(int index) {
            var bone = bones[index];
            var go = new GameObject(bone.name);
            var newBone = go.transform;
            go.name = GetUniqueNameForBone(newBone, bone);
            newBone.SetParent(bone, false);
            Undo.RegisterCreatedObjectUndo(go, "Create Bone");
            ReplaceBone(index, newBone);
        }

        readonly struct BoneCoverage {
            public readonly int refCount;
            public readonly float coverage;

            public BoneCoverage(int refCount, float coverage) {
                this.refCount = refCount;
                this.coverage = coverage;
            }

            public static BoneCoverage operator +(BoneCoverage a, float weight) =>
                new BoneCoverage(a.refCount + 1, a.coverage + weight);
        }

        class PoseModeState : IDisposable {
            readonly SkinnedMeshRenderer target;
            readonly GameObject targetObject;
            readonly Transform targetTransform;
            public readonly Transform[] bones;
            readonly Matrix4x4[] poses;
            readonly Mesh previewMesh;
            Mesh mesh;

            public Mesh TargetMesh => mesh;

            public PoseModeState(SkinnedMeshRenderer target, Transform[] bones = null) {
                this.target = target;
                targetObject = target.gameObject;
                targetTransform = target.transform;
                mesh = target.sharedMesh;
                if (mesh == null) return;
                if (bones == null) bones = target.bones;
                if (bones == null || bones.Length == 0) return;
                this.bones = bones;
                poses = new Matrix4x4[bones.Length];
                for (int i = 0; i < bones.Length; i++) {
                    var bone = bones[i];
                    poses[i] = bone != null ? bone.localToWorldMatrix : Matrix4x4.identity;
                }
                previewMesh = new Mesh {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                target.BakeMesh(previewMesh, true);
                target.forceRenderingOff = true;
                SceneView.duringSceneGui += DrawPreview;
            }

            public bool Apply() {
                if (mesh == null || bones == null) return false;
                var bindposes = mesh.bindposes;
                bool modified = false;
                for (int i = 0, length = Mathf.Min(poses.Length, bones.Length); i < length; i++) {
                    if (bones[i] == null) continue;
                    var m = bones[i].worldToLocalMatrix * poses[i];
                    if (m.isIdentity) continue;
                    bindposes[i] = m * bindposes[i];
                    modified = true;
                }
                if (!modified) return false;
                var modifiedMesh = Instantiate(mesh);
                modifiedMesh.name = mesh.name;
                modifiedMesh.bindposes = bindposes;
                modifiedMesh.UploadMeshData(false);
                target.sharedMesh = modifiedMesh;
                mesh = modifiedMesh;
                target.BakeMesh(previewMesh, true);
                return true;
            }

            void DrawPreview(SceneView sceneView) {
                if (previewMesh == null || !targetObject.activeInHierarchy || !target.enabled) return;
                var materials = target.sharedMaterials;
                var matrix = targetTransform.localToWorldMatrix;
                var layer = targetObject.layer;
                if (materials == null || materials.Length == 0) return;
                for (int i = 0; i < materials.Length; i++) {
                    var material = materials[i];
                    if (material == null) continue;
                    Graphics.DrawMesh(previewMesh, matrix, material, layer, sceneView.camera, i);
                }
            }

            public void Dispose() {
                if (previewMesh != null) DestroyImmediate(previewMesh);
                target.forceRenderingOff = false;
                SceneView.duringSceneGui -= DrawPreview;
            }
        }
    }
}
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
        const string APPLY_ICON = "SaveAs";
        const string AUTO_APPLY_ICON = "SaveFromPlay";
        const string REFRESH_ICON = "Refresh";
        const string NO_WEIGHT_ICON = "UnLinked";
        const string MISSING_ICON = "Invalid";
        const string AVATAR_ICON = "Avatar Icon";
        const string ROOT_ICON = "AvatarPivot";
        const string ADD_ICON = "Toolbar Plus";
        const string MOVE_ICON = "Animation.FilterBySelection";
        const string REMOVE_ICON = "Toolbar Minus";
        const string REPOSE_ICON = "TransformTool";
        const string REPOSE_CANCEL_ICON = "TransformTool On";
        const string WARN_ICON = "Warning";
        const string INFO_ICON = "console.infoicon";
        public static readonly Color unReferencedBoneColor = new Color(0, 0, 0, 0);
        public static readonly Color noCoverageBoneColor = new Color(0, 0, 0.5F, 0);
        static readonly GUIContent tempContent = new GUIContent();
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
        static string[] humanBoneNameTooltips;
        static bool autoApply;
        static Transform[] copiedBones;
        static dynamic boneRenderer;
        static Action refreshBoneCoverageMap;
        Dictionary<Transform, int> humanoidBones;
        SkinnedMeshRenderer target;
        Mesh mesh;
        Transform[] bones;
        static float maxCoverage;
        static bool shouldRefreshBoneCoverageMap;
        static Transform selectedBone;
        BoneCoverage[] boneInfos;
        string[] boneInfoStrings;
        PoseModeState poseModeState;
        ReorderableList list;
        Vector2 scrollPos;
        bool isDirty;

        static GUIContent RefreshContent => GetGUIContent(null, "Refresh", REFRESH_ICON);

        static GUIContent ReposeContent => GetGUIContent(null, "Repose Bones (Modifies Skinned Mesh)", REPOSE_ICON);

        static GUIContent AutoApplyContent => GetGUIContent(null, "Auto apply changes to target skinned mesh renderer.", AUTO_APPLY_ICON);

        static GUIContent ApplyContent => GetGUIContent(null, "Apply changes to skinned mesh renderer.", APPLY_ICON);

        static GUIContent ApplyReposeContent => GetGUIContent(null, "Save reposed skinned mesh.", APPLY_ICON);

        static GUIContent DiscardReposeContent => GetGUIContent(null, "Discard reposed skinned mesh.", REPOSE_CANCEL_ICON);

        static GUIContent FillEmptyContent => GetGUIContent(null, "Fill empty bone references.", ADD_ICON);

        static GUIContent ClearEmptyContent => GetGUIContent(null, "Clear empty bone references.", REMOVE_ICON);

        static GUIContent MoveAllContent => GetGUIContent(null, "Try moving all bones to bindpose.", MOVE_ICON);
        
        static GUIContent ReassignDuplicateContent => GetGUIContent("Reassign Dup.", "Reassign duplicated bones.");

        static GUIContent RenameDuplicateContent => GetGUIContent("Rename Dup.", "Rename duplicated bones.");

        static BoneEditorWindow() => SceneView.duringSceneGui += OnSceneGUI;

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
            if (humanBoneNameTooltips == null) {
                var humanBoneNames = MecanimUtils.HumanBoneNames;
                humanBoneNameTooltips = new string[humanBoneNames.Length];
                for (int i = 0; i < humanBoneNames.Length; i++) {
                    var name = humanBoneNames[i];
                    humanBoneNameTooltips[i] = $"This is {ObjectNames.NicifyVariableName(name)} bone (humanoid).";
                }
            }
            if (boneRenderer == null) boneRenderer = Limitless.Construct("UnityEditor.Handles+BoneRenderer, UnityEditor");
            if (target == null) return;
            minSize = new Vector2(500, 100);
            mesh = target.sharedMesh;
            RefreshBones();
            Undo.undoRedoPerformed += OnUndoRedo;
            autoApply = EditorPrefs.GetBool(EDITOR_PREFS_AUTOAPPLY, true);
            refreshBoneCoverageMap += RefreshBoneCoverageMap;
            isDirty = false;
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

        void UpdateTitle() {
            var content = titleContent;
            content.text = poseModeState != null ? "Bone Editor (Reposing)" : isDirty ? "Bone Editor*" : "Bone Editor";
            titleContent = content;
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
                if (GUILayout.Button(RefreshContent, EditorStyles.toolbarButton, noExpandWidth))
                    RefreshBones();
                if (poseModeState == null) {
                    if (GUILayout.Button(ReposeContent, EditorStyles.toolbarButton, noExpandWidth))
                        EnablePoseMode();
                    using (var changed = new EditorGUI.ChangeCheckScope()) {
                        autoApply = GUILayout.Toggle(autoApply, AutoApplyContent, EditorStyles.toolbarButton, noExpandWidth);
                        if (changed.changed) EditorPrefs.SetBool(EDITOR_PREFS_AUTOAPPLY, autoApply);
                    }
                    using (new EditorGUI.DisabledScope(autoApply))
                        if (GUILayout.Button(ApplyContent, EditorStyles.toolbarButton, noExpandWidth))
                            ApplyBones();
                } else {
                    if (GUILayout.Button(DiscardReposeContent, EditorStyles.toolbarButton, noExpandWidth))
                        CancelPoseMode();
                    using (new EditorGUI.DisabledScope(true))
                        GUILayout.Label(AutoApplyContent, EditorStyles.toolbarButton, noExpandWidth);
                    if (GUILayout.Button(ApplyReposeContent, EditorStyles.toolbarButton, noExpandWidth))
                        ApplyPoseMode();
                }
                EditorGUILayout.Space();
                if (GUILayout.Button(FillEmptyContent, EditorStyles.toolbarButton, noExpandWidth))
                    FillEmpty();
                if (GUILayout.Button(ClearEmptyContent, EditorStyles.toolbarButton, noExpandWidth))
                    ClearEmpty();
                if (GUILayout.Button(MoveAllContent, EditorStyles.toolbarButton, noExpandWidth))
                    SetBindpose();
                if (GUILayout.Button(ReassignDuplicateContent, EditorStyles.toolbarButton, noExpandWidth))
                    ReassignDuplicatedBones();
                if (GUILayout.Button(RenameDuplicateContent, EditorStyles.toolbarButton, noExpandWidth))
                    RenameDuplicatedBones();
                GUILayout.FlexibleSpace();
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
            if (humanoidBones == null) humanoidBones = new Dictionary<Transform, int>();
            var bones = MecanimUtils.FetchHumanoidBodyBones(avatar, animator.transform);
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) humanoidBones.Add(bones[i], i);
        }

        static void OnSceneGUI(SceneView sceneView) {
            if (Event.current.type != EventType.Repaint || !sceneView.drawGizmos) return;
            RefreshBoneCoverageMapIfNeeded();
            if (boneRenderer == null) return;
            boneRenderer.ClearInstances();
            foreach (var (current, child) in boneGizmoMap) {
                if (current == null) continue;
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
            if (!autoApply) return;
            RefreshBones();
            Repaint();
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
            } else {
                isDirty = false;
                UpdateTitle();
            }
            boneInfos = new BoneCoverage[boneCount];
            float totalCoverage = 0;
            foreach (var boneWeight in mesh.GetAllBoneWeights()) {
                totalCoverage += boneWeight.weight;
                boneInfos[boneWeight.boneIndex] += boneWeight.weight;
            }
            Debug.Assert(Mathf.Approximately(totalCoverage, mesh.vertexCount), $"Bone weight coverage {totalCoverage} != vertex count {mesh.vertexCount}", mesh);
            if (boneInfoStrings == null || boneInfoStrings.Length < boneInfos.Length)
                boneInfoStrings = new string[boneInfos.Length];
            for (int i = 0; i < boneInfos.Length; i++) {
                ref var boneInfo = ref boneInfos[i];
                boneInfoStrings[i] = $"{i} ({boneInfo.refCount}, {boneInfo.coverage / totalCoverage:0.##%})";
            }
            list = new ReorderableList(bones, typeof(Transform), true, true, false, false) {
                drawElementCallback = OnListDrawElement,
                drawHeaderCallback = DoNotDraw,
                drawFooterCallback = DoNotDraw,
                onReorderCallback = OnListReorder,
                elementHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * 0.5F,
                showDefaultBackground = false,
                headerHeight = 0,
                footerHeight = 0,
            };
            OnHierarchyChange();
            shouldRefreshBoneCoverageMap = true;
        }

        void OnListDrawElement(Rect rect, int index, bool isActive, bool isFocused) {
            var bone = bones[index];
            rect.y += EditorGUIUtility.standardVerticalSpacing;
            var size = EditorGUIUtility.singleLineHeight;
            var rect2 = rect;
            rect2.width -= size * 2;
            rect2.height = size;
            var contentColor = GUI.contentColor;
            float coverage = boneInfos[index].coverage;
            if (coverage == 0) {
                var newContentColor = contentColor;
                newContentColor.a *= 0.5F;
                GUI.contentColor = newContentColor;
            }
            var iconRect = rect2;
            iconRect.x = rect2.xMin + EditorGUIUtility.labelWidth;
            iconRect.width = size;
            rect2 = EditorGUI.PrefixLabel(rect2, GetGUIContent(boneInfoStrings[index]));
            if (coverage <= 0)
                DrawIcon(ref iconRect, "This bone has no weights. In most cases you can safely remove this bone reference.", NO_WEIGHT_ICON);
            else if (bone == null)
                DrawIcon(ref iconRect, "This bone is not assigned and it has weights. Parts of the mesh binded to this bone will breaks.", MISSING_ICON);
            else {
                var rootBone = target.rootBone;
                if (rootBone == null) rootBone = target.transform;
                if (!bone.IsChildOf(rootBone)) DrawIcon(ref iconRect, "This bone is not under root bone. This is not recommend unless it is intentional.", WARN_ICON);
            }
            using (var changed = new EditorGUI.ChangeCheckScope()) {
                bone = EditorGUI.ObjectField(rect2, bone, typeof(Transform), true) as Transform;
                if (changed.changed) {
                    ReplaceBone(index, bone);
                    ApplyBones("Replace Bone Transform");
                }
            }
            if (bone != null) {
                iconRect = new Rect(
                    rect2.xMax - size - 4,
                    rect2.y + 1,
                    size - 2,
                    size - 2
                );
                if (humanoidBones != null && humanoidBones.TryGetValue(bone, out int boneIndex))
                    DrawIcon(ref iconRect, humanBoneNameTooltips[boneIndex], AVATAR_ICON);
                if (target.rootBone == bone)
                    DrawIcon(ref iconRect, "This is the root bone.", ROOT_ICON);
            }
            GUI.contentColor = contentColor;
            rect2.xMin = rect2.xMax + 2;
            rect2.width = size;
#if UNITY_2021_2_OR_NEWER
            var iconButtonStyle = EditorStyles.iconButton;
#else
            var iconButtonStyle = EditorStyles.label;
#endif
            if (bone == null) {
                if (GUI.Button(rect2, GetGUIContent(null, "Create new bone transform at bindpose.", ADD_ICON), iconButtonStyle)) {
                    AutoSetBone(index, true);
                    ApplyBones("Create Bone Transform", true);
                }
            } else {
                if (GUI.Button(rect2, GetGUIContent(null, "Try move bone transform to bindpose.", MOVE_ICON), iconButtonStyle)) {
                    AutoSetBone(index, true);
                    ApplyBones("Move Bone Transform", true);
                }
            }
            rect2.x += 16;
            if (bone != null) {
                bool isDuplicateName = !walkedNames.Add(bone.name);
                bool isDuplicateBone = !walkedBones.Add(bone);
                if (isDuplicateBone) {
                    if (GUI.Button(rect2, GetGUIContent(null, "This bone is duplicated. You may need to reassign a new bone in some cases.", INFO_ICON), iconButtonStyle) &&
                        EditorUtility.DisplayDialog("Reassign Bone", $"Reassign bone \"{bone.name}\"?", "Yes", "No")) {
                        ReassignBone(index);
                        ApplyBones("Replace Bone Transform");
                    }
                } else if (isDuplicateName) {
                    if (GUI.Button(rect2, GetGUIContent(null, "This bone has the same name with another bone. You may need to rename it in some cases.", INFO_ICON), iconButtonStyle)) {
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
                go.name = GetUniqueNameForBone(bone, rootBone);
                bone.SetParent(rootBone, false);
                ReplaceBone(index, bone);
                var parentBone = rootBone.parent;
                offset = parentBone != null ? parentBone.localToWorldMatrix : Matrix4x4.identity;
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
                if (parentBone == null) {
                    parentBone = rootBone.parent;
                    offset = parentBone != null ? parentBone.localToWorldMatrix : Matrix4x4.identity;
                } else {
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
                isDirty = false;
            } else 
                isDirty = true;
            UpdateTitle();
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
            foreach (var i in bones.GetSortedIndices(new TransformComparer(TransformComparer.Mode.DepthFirst)))
                if (bones[i] != null && walkedBones.Add(bones[i])) AutoSetBone(i, true);
            ApplyBones("Move Bones To Bindpose", true);
        }

        void ReassignDuplicatedBones() {
            walkedBones.Clear();
            foreach (var i in bones.GetSortedIndices(new TransformComparer(TransformComparer.Mode.DepthFirst)))
                if (bones[i] != null && walkedBones.Add(bones[i])) ReassignBone(i);
            ApplyBones("Reassign Duplicated Bones", true);
        }

        void RenameDuplicatedBones() {
            walkedNames.Clear();
            walkedBones.Clear();
            foreach (var i in bones.GetSortedIndices(new TransformComparer(TransformComparer.Mode.DepthFirst))) {
                var bone = bones[i];
                if (bone == null || !walkedBones.Add(bone)) continue;
                var name = bone.name;
                if (!walkedNames.Add(name)) continue;
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
            UpdateTitle();
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
                    $"{target.name} Reposed Mesh",
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
            isDirty = false;
            UpdateTitle();
        }

        void CancelPoseMode() {
            if (poseModeState == null) return;
            poseModeState.Dispose();
            poseModeState = null;
            isDirty = false;
            UpdateTitle();
        }

        Transform[] GetSortedBones() {
            var sortedBones = new Transform[bones.Length];
            Array.Copy(bones, sortedBones, bones.Length);
            Array.Sort(sortedBones, new TransformComparer(TransformComparer.Mode.DepthFirst));
            return sortedBones;
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

        static void DrawIcon(ref Rect iconRect, string tooltip, string iconName) {
            iconRect.x -= iconRect.width;
            GUI.Label(iconRect, GetGUIContent(null, tooltip, iconName), EditorStyles.label);
        }

        static GUIContent GetGUIContent(string title = null, string tooltip = null, string iconName = null) {
            tempContent.text = title ?? string.Empty;
            tempContent.tooltip = tooltip ?? string.Empty;
            tempContent.image = string.IsNullOrEmpty(iconName) ? null : EditorGUIUtility.IconContent(iconName).image;
            return tempContent;
        }

        string GetUniqueNameForBone(Transform bone, Transform parent = null) {
            var orgName = bone.name;
            var newName = orgName;
            if (parent == null) parent = bone.parent;
            if (parent != null) {
                bone.name = $"{orgName}_";
                newName = GameObjectUtility.GetUniqueNameForSibling(parent, orgName);
                bone.name = orgName;
            }
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
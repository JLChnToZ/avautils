using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;

public class AdvancedFittingRoom : EditorWindow {
    static readonly Func<Transform, Dictionary<Transform, bool>, Dictionary<int, Transform>> MapBones;
    readonly HashSet<SkinnedMeshRenderer> selectedSkinnedMeshRenderers = new HashSet<SkinnedMeshRenderer>();
    readonly Dictionary<Transform, (Transform dest, BoneMergeOptions options)> boneMap = new Dictionary<Transform, (Transform, BoneMergeOptions)>();
    readonly Stack<(Transform, int)> childWalker = new Stack<(Transform, int)>();
    readonly Stack<Transform> parentStack = new Stack<Transform>();
    readonly Dictionary<Transform, HumanBodyBones> sourceHumanoidBodyBones = new Dictionary<Transform, HumanBodyBones>();
    readonly Dictionary<HumanBodyBones, Transform> destHumanoidBodyBones = new Dictionary<HumanBodyBones, Transform>();
    readonly HashSet<(Transform, Transform)> canMergeBones = new HashSet<(Transform, Transform)>();
    Transform sourceRootBone, destRootBone;
    Vector2 skinnedMeshRendererScrollPosition, boneMapScrollPosition;
    bool hasSourceHumanoidBodyBones, hasDestHumanoidBodyBones;

    static AdvancedFittingRoom() {
        var mapBonesMethod = Type.GetType("UnityEditor.AvatarAutoMapper, UnityEditor", false)?.GetMethod(
            "MapBones",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new [] { typeof(Transform), typeof(Dictionary<Transform, bool>) },
            null
        );
        if (mapBonesMethod != null)
            MapBones = Delegate.CreateDelegate(
                typeof(Func<Transform, Dictionary<Transform, bool>, Dictionary<int, Transform>>),
                mapBonesMethod
            ) as Func<Transform, Dictionary<Transform, bool>, Dictionary<int, Transform>>;
    }

    [MenuItem("Tools/JLChnToZ/Advanced Fitting Room")]
    static void ShowWindow() {
        var window = GetWindow<AdvancedFittingRoom>();
        window.titleContent = new GUIContent("Advanced Fitting Room");
        window.Show();
    }

    static Dictionary<HumanBodyBones, Transform> GuessHumanoidBodyBones(Transform root, IEnumerable<Transform> validBones = null) {
        Dictionary<HumanBodyBones, Transform> result;
        var animator = root.GetComponentInParent<Animator>();
        if (animator != null && animator.avatar != null && animator.avatar.isHuman) {
            result = new Dictionary<HumanBodyBones, Transform>();
            for (HumanBodyBones bone = 0; bone < HumanBodyBones.LastBone; bone++) {
                var boneTransform = animator.GetBoneTransform(bone);
                if (boneTransform != null) result[bone] = boneTransform;
            }
            return result;
        }
        if (MapBones == null) {
            Debug.LogWarning("Can not find AvatarAutoMapper, humanoid bone mapping is not available.");
            return null;
        }
        var vaildBoneMap = new Dictionary<Transform, bool>();
        if (validBones != null)
            foreach (var bone in validBones)
                vaildBoneMap[bone] = true;
        else {
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0) {
                var current = stack.Pop();
                vaildBoneMap[current] = true;
                foreach (Transform child in current) stack.Push(child);
            }
        }
        var rawResult = MapBones(root, vaildBoneMap);
        result = new Dictionary<HumanBodyBones, Transform>();
        foreach (var kv in rawResult) result[(HumanBodyBones)kv.Key] = kv.Value;
        return result;
    }

    void OnEnable() {
        selectedSkinnedMeshRenderers.Clear();
        boneMap.Clear();
        sourceHumanoidBodyBones.Clear();
        destHumanoidBodyBones.Clear();
        canMergeBones.Clear();
        hasSourceHumanoidBodyBones = false;
        hasDestHumanoidBodyBones = false;
        AddSelectedSkinnedMeshRenderers(true);
    }

    void OnGUI() {
        using (new EditorGUILayout.HorizontalScope()) {
            using (new EditorGUILayout.VerticalScope(GUI.skin.box)) {
                using (var scroll = new EditorGUILayout.ScrollViewScope(skinnedMeshRendererScrollPosition)) {
                    skinnedMeshRendererScrollPosition = scroll.scrollPosition;
                    SkinnedMeshRenderer pendingRemoveSkinnedMeshRenderer = null;
                    foreach (var entry in selectedSkinnedMeshRenderers) {
                        if (entry == null) continue;
                        using (new EditorGUILayout.HorizontalScope()) {
                            using (new EditorGUI.DisabledScope(true))
                                EditorGUILayout.ObjectField(entry, typeof(SkinnedMeshRenderer), true);
                            if (GUILayout.Button("Remove"))
                                pendingRemoveSkinnedMeshRenderer = entry;
                        }
                    }
                    if (pendingRemoveSkinnedMeshRenderer != null) selectedSkinnedMeshRenderers.Remove(pendingRemoveSkinnedMeshRenderer);
                    GUILayout.FlexibleSpace();
                }
                using (new EditorGUILayout.HorizontalScope()) {
                    if (GUILayout.Button("Add Selection"))
                        AddSelectedSkinnedMeshRenderers();
                    if (GUILayout.Button("Remove All", GUILayout.ExpandWidth(false)))
                        selectedSkinnedMeshRenderers.Clear();
                }
            }
            using (new EditorGUILayout.VerticalScope(GUI.skin.box)) {
                using (var changed = new EditorGUI.ChangeCheckScope()) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        sourceRootBone = EditorGUILayout.ObjectField("Source", sourceRootBone, typeof(Transform), true) as Transform;
                        destRootBone = EditorGUILayout.ObjectField("Destination", destRootBone, typeof(Transform), true) as Transform;
                        using (new EditorGUI.DisabledScope(sourceRootBone == null || destRootBone == null))
                            if (GUILayout.Button("Match position and rotation", GUILayout.ExpandWidth(false))) {
                                Undo.RecordObject(sourceRootBone, "Match position and rotation");
                                sourceRootBone.position = destRootBone.position;
                                sourceRootBone.rotation = destRootBone.rotation;
                            }
                    }
                    if (sourceRootBone == null || destRootBone == null) {
                        EditorGUILayout.HelpBox("Please select source and destination root bone.", MessageType.Info);
                    } else if (sourceRootBone == destRootBone) {
                        EditorGUILayout.HelpBox("Source and destination root bone can not be same.", MessageType.Error);
                    } else if (changed.changed) {
                        boneMap.Clear();
                        RefreshBoneMap();
                    }
                }
                BoneMergeOptions? checkOptions = null;
                bool hasMultipleOptions = false;
                using (var scroll = new EditorGUILayout.ScrollViewScope(boneMapScrollPosition)) {
                    boneMapScrollPosition = scroll.scrollPosition;
                    childWalker.Clear();
                    childWalker.Push((sourceRootBone, 0));
                    while (childWalker.Count > 0) {
                        var (current, level) = childWalker.Pop();
                        if (current == null) continue;
                        using (new EditorGUILayout.HorizontalScope()) {
                            using (new EditorGUI.DisabledScope(true))
                                EditorGUILayout.ObjectField(current, typeof(Transform), true);
                            using (var changed = new EditorGUI.ChangeCheckScope())
                                if (boneMap.TryGetValue(current, out var dest)) {
                                    dest.dest = EditorGUILayout.ObjectField(dest.dest, typeof(Transform), true) as Transform;
                                    dest.options = (BoneMergeOptions)EditorGUILayout.EnumFlagsField(dest.options, GUILayout.ExpandWidth(false));
                                    if (changed.changed) boneMap[current] = dest;
                                    if (checkOptions == null)
                                        checkOptions = dest.options;
                                    else if (checkOptions != dest.options)
                                        hasMultipleOptions = true;
                                } else {
                                    var destBone = EditorGUILayout.ObjectField(null, typeof(Transform), true) as Transform;
                                    using (new EditorGUI.DisabledScope(true))
                                        EditorGUILayout.EnumFlagsField(BoneMergeOptions.None, GUILayout.ExpandWidth(false));
                                    if (changed.changed && destBone != null) {
                                        boneMap[current] = (destBone, BoneMergeOptions.None);
                                        if (checkOptions == null)
                                            checkOptions = BoneMergeOptions.None;
                                        else if (checkOptions != BoneMergeOptions.None)
                                            hasMultipleOptions = true;
                                    }
                                }
                        }
                        for (int i = current.childCount - 1; i >= 0; i--)
                            childWalker.Push((current.GetChild(i), level + 1));
                    }
                    GUILayout.FlexibleSpace();
                }
                using (new EditorGUILayout.HorizontalScope()) {
                    bool wasMultiValue = EditorGUI.showMixedValue;
                    EditorGUI.showMixedValue = hasMultipleOptions;
                    using (var changed = new EditorGUI.ChangeCheckScope()) {
                        var options = (BoneMergeOptions)EditorGUILayout.EnumFlagsField("Bone Merge Options", checkOptions.GetValueOrDefault());
                        if (changed.changed) {
                            var keys = new Transform[boneMap.Count];
                            boneMap.Keys.CopyTo(keys, 0);
                            foreach (var key in keys) {
                                var value = boneMap[key];
                                value.options = options;
                                boneMap[key] = value;
                            }
                        }
                    }
                    EditorGUI.showMixedValue = wasMultiValue;
                    if (GUILayout.Button("Combine All Matched", GUILayout.ExpandWidth(false)))
                        foreach (var (src, dest) in canMergeBones)
                            if (src != null && boneMap.TryGetValue(src, out var value) && value.dest == dest) {
                                value.options = BoneMergeOptions.Combine;
                                boneMap[src] = value;
                            }
                    if (GUILayout.Button("Parent All Matched", GUILayout.ExpandWidth(false)))
                        foreach (var (src, dest) in canMergeBones)
                            if (src != null && boneMap.TryGetValue(src, out var value) && value.dest == dest) {
                                value.options = BoneMergeOptions.None;
                                boneMap[src] = value;
                            }
                    if (GUILayout.Button("Apply")) ApplyBoneTransfer();
                    if (GUILayout.Button("Reset", GUILayout.ExpandWidth(false))) {
                        boneMap.Clear();
                        RefreshBoneMap();
                    }
                }
            }
        }
    }

    void AddSelectedSkinnedMeshRenderers(bool refreshRootBone = false) {
        selectedSkinnedMeshRenderers.UnionWith(Selection.GetFiltered<SkinnedMeshRenderer>(SelectionMode.Editable | SelectionMode.Deep));
        if (refreshRootBone) sourceRootBone = null;
        if (sourceRootBone == null) AutoFetchRootBone();
    }

    void AutoFetchRootBone() {
        foreach (var skinnedMeshRenderer in selectedSkinnedMeshRenderers) {
            var currentRootBone = skinnedMeshRenderer.rootBone;
            if (currentRootBone == null) currentRootBone = skinnedMeshRenderer.transform;
            if (sourceRootBone == null || sourceRootBone.IsChildOf(currentRootBone))
                sourceRootBone = currentRootBone;
        }
        RefreshBoneMap();
    }

    void RefreshBoneMap() {
        if (sourceRootBone == null || destRootBone == null) return;
        sourceHumanoidBodyBones.Clear();
        hasSourceHumanoidBodyBones = false;
        destHumanoidBodyBones.Clear();
        hasDestHumanoidBodyBones = false;
        canMergeBones.Clear();
        foreach (var skinnedMeshRenderer in selectedSkinnedMeshRenderers) {
            var bones = skinnedMeshRenderer.bones;
            if (bones == null || bones.Length == 0) continue;
            foreach (var bone in bones) {
                if (bone == null || boneMap.ContainsKey(bone)) continue;
                var parent = FindCorrespondingBone(bone);
                if (parent == null) {
                    parent = FindCorrespondingBone(bone.parent);
                    if (parent == null) continue;
                } else canMergeBones.Add((bone, parent));
                var mergeOptions = BoneMergeOptions.None;
                if (Vector3.Distance(bone.position, parent.position) < 0.01f)
                    mergeOptions |= BoneMergeOptions.ZeroTranslation;
                if (Quaternion.Angle(bone.rotation, parent.rotation) < 0.01f)
                    mergeOptions |= BoneMergeOptions.ZeroRotation;
                if (Vector3.Distance(bone.lossyScale, parent.lossyScale) < 0.01f)
                    mergeOptions |= BoneMergeOptions.ZeroScale;
                boneMap.Add(bone, (parent, mergeOptions));
            }
        }
    }

    void InitSourceHumanoidBodyBones() {
        if (hasSourceHumanoidBodyBones || sourceRootBone == null) return;
        hasSourceHumanoidBodyBones = true;
        sourceHumanoidBodyBones.Clear();
        var mapping = GuessHumanoidBodyBones(sourceRootBone);
        if (mapping != null) foreach (var kv in mapping) sourceHumanoidBodyBones[kv.Value] = kv.Key;
    }

    void InitDestHumanoidBodyBones() {
        if (hasDestHumanoidBodyBones || destRootBone == null) return;
        hasDestHumanoidBodyBones = true;
        destHumanoidBodyBones.Clear();
        var mapping = GuessHumanoidBodyBones(destRootBone);
        if (mapping != null) foreach (var kv in mapping) destHumanoidBodyBones[kv.Key] = kv.Value;
    }

    Transform FindCorrespondingBone(Transform bone) {
        if (bone == null) return null;
        if (bone == sourceRootBone) return destRootBone;
        lock (parentStack) {
            parentStack.Clear();
            var destBone = bone;
            while (destBone != null && destBone != sourceRootBone) {
                parentStack.Push(destBone);
                destBone = destBone.parent;
            }
            destBone = destRootBone;
            while (parentStack.Count > 0) {
                var current = parentStack.Pop();
                var found = false;
                foreach (Transform child in destBone)
                    if (child.name == current.name) {
                        destBone = child;
                        found = destBone;
                        break;
                    }
                if (found) continue;
                foreach (Transform child in destBone)
                    if (child.name.StartsWith(current.name)) {
                        destBone = child;
                        found = destBone;
                        break;
                    }
                if (found) continue;
                InitSourceHumanoidBodyBones();
                found = sourceHumanoidBodyBones.TryGetValue(current, out var boneType);
                if (found) {
                    InitDestHumanoidBodyBones();
                    found = destHumanoidBodyBones.TryGetValue(boneType, out destBone);
                    if (found) continue;
                }
                parentStack.Clear();
                return null;
            }
            return destBone;
        }
    }

    void ApplyBoneTransfer() {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        var processedBones = new Dictionary<Transform, Transform>();
        foreach (var skinnedMeshRenderer in selectedSkinnedMeshRenderers) {
            var bones = skinnedMeshRenderer.bones;
            if (bones == null || bones.Length == 0) continue;
            bool boneChanged = false;
            var rootBone = skinnedMeshRenderer.rootBone;
            if (rootBone == null) rootBone = skinnedMeshRenderer.transform;
            var newRootBone = FindCorrespondingBone(rootBone);
            if (newRootBone != rootBone) {
                skinnedMeshRenderer.rootBone = newRootBone;
                boneChanged = true;
            }
            for (int i = 0; i < bones.Length; i++) {
                var bone = bones[i];
                if (bone == null) continue;
                if (!boneMap.TryGetValue(bone, out var dest) || dest.dest == null) {
                    var parent = bone.parent;
                    if (parent != null &&
                        boneMap.TryGetValue(parent, out dest) &&
                        dest.dest != parent &&
                        dest.options == BoneMergeOptions.Combine &&
                        !CheckAndSetParent(bone, dest.dest)) {
                        Undo.CollapseUndoOperations(undoGroup);
                        Undo.PerformUndo();
                        return;
                    }
                    continue;
                }
                if (dest.options == BoneMergeOptions.Combine) {
                    processedBones[bones[i]] = dest.dest;
                    bones[i] = dest.dest;
                    boneChanged = true;
                    continue;
                }
                if (!processedBones.ContainsKey(bone)) {
                    processedBones[bone] = bone;
                    if (!CheckAndSetParent(bone, dest.dest))  {
                        Undo.CollapseUndoOperations(undoGroup);
                        Undo.PerformUndo();
                        return;
                    }
                    if (dest.options.HasFlag(BoneMergeOptions.ZeroTranslation)) {
                        Undo.RecordObject(bone, "Bone Merge");
                        bone.localPosition = Vector3.zero;
                    }
                    if (dest.options.HasFlag(BoneMergeOptions.ZeroRotation)) {
                        Undo.RecordObject(bone, "Bone Merge");
                        bone.localRotation = Quaternion.identity;
                    }
                    if (dest.options.HasFlag(BoneMergeOptions.ZeroScale)) {
                        Undo.RecordObject(bone, "Bone Merge");
                        bone.localScale = Vector3.one;
                    }
                }
            }
            for (int i = 0; i < bones.Length; i++) {
                if (bones[i] == null) continue;
                var parent = bones[i].parent;
                if (parent == null || !processedBones.TryGetValue(parent, out var dest) || dest == parent) continue;
                if (!CheckAndSetParent(bones[i], dest)) {
                    Undo.CollapseUndoOperations(undoGroup);
                    Undo.PerformUndo();
                    return;
                }
            }
            if (boneChanged) {
                Undo.RecordObject(skinnedMeshRenderer, "Bone Merge");
                skinnedMeshRenderer.bones = bones;
            }
        }
        Undo.CollapseUndoOperations(undoGroup);
    }

    bool CheckAndSetParent(Transform child, Transform parent) {
        if (child == null || parent == null) return true;
        if (child.parent == parent) return true;
        if (PrefabUtility.IsPartOfAnyPrefab(child)) {
            var outerMost = PrefabUtility.GetOutermostPrefabInstanceRoot(child);
            if (EditorUtility.DisplayDialog("Unpack Prefab", $"Bone {child.name} is part of prefab, do you want to unpack prefab?", "Yes", "No")) {
                Undo.RecordObject(outerMost, "Bone Merge");
                PrefabUtility.UnpackPrefabInstance(outerMost, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            } else
                return false;
        }
        Undo.SetTransformParent(child, parent, "Bone Merge");
        return true;
    }

    [Flags]
    enum BoneMergeOptions {
        None = 0,
        ZeroTranslation = 0x1,
        ZeroRotation = 0x2,
        ZeroScale = 0x4,
        Combine = ZeroTranslation | ZeroRotation | ZeroScale,
    }
}

using UnityEngine;

#if VRC_SDK_VRCSDK3 && VRC_SDK3_AVATARS
using System;
using System.Collections.Generic;
using UnityEditor;
using VRC.SDKBase;
using VRC.Dynamics;

using static UnityEngine.Object;

public static class VRCPhysBoneHelperX {
    [MenuItem("Tools/JLChnToZ/Move Phys Bones")]
    public static void MovePhysBones() {
        var pbMapping = new HashSet<VRCPhysBoneBase>();
        VRC_AvatarDescriptor avatarDescriptor = null;
        var pbs = new List<VRCPhysBoneBase>();
        foreach (var go in Selection.GetFiltered<GameObject>(SelectionMode.Deep)) {
            go.GetComponents(pbs);
            pbMapping.UnionWith(pbs);
            if (avatarDescriptor == null && pbs.Count > 0) avatarDescriptor = pbs[0].GetComponentInParent<VRC_AvatarDescriptor>();
        }
        if (pbMapping.Count <= 0) return;
        var scene = Selection.activeGameObject.scene;
        var rootTransform = avatarDescriptor == null ? Selection.activeTransform.parent : avatarDescriptor.transform;
        var skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
        var pbMapping2 = new Dictionary<VRCPhysBoneBase, Transform>();
        var pbHasNoReference = new HashSet<VRCPhysBoneBase>(pbMapping);
        var pbHasMultiple = new HashSet<VRCPhysBoneBase>();
        var transformWalker = new Queue<Transform>();
        var ignoreTransforms = new HashSet<Transform>();
        var transformHasReference = new Dictionary<Transform, VRCPhysBoneBase>();
        var checkedTransforms = new HashSet<Transform>();
        foreach (var root in scene.GetRootGameObjects()) {
            root.GetComponentsInChildren(true, skinnedMeshRenderers);
            foreach (var skinnedMeshRenderer in skinnedMeshRenderers) {
                var mesh = skinnedMeshRenderer.sharedMesh;
                if (mesh == null) continue;
                Transform[] bones = null;
                var rendererTransform = skinnedMeshRenderer.transform;
                foreach (var pb in pbMapping) {
                    var pbTransform = pb.rootTransform;
                    if (pbTransform == null) pbTransform = pb.transform;
                    transformWalker.Clear();
                    transformWalker.Enqueue(pbTransform);
                    ignoreTransforms.Clear();
                    ignoreTransforms.UnionWith(pb.ignoreTransforms);
                    while (transformWalker.Count > 0) {
                        pbTransform = transformWalker.Dequeue();
                        foreach (Transform child in pbTransform)
                            if (!ignoreTransforms.Contains(child))
                                transformWalker.Enqueue(child);
                        if (transformHasReference.TryGetValue(pbTransform, out var otherPb) && otherPb != pb && checkedTransforms.Add(pbTransform))
                            Debug.LogWarning($"{pbTransform.name} has been referenced by multiple phys bones.", pbTransform);
                        else
                            transformHasReference[pbTransform] = pb;
                        if (pbHasMultiple.Contains(pb)) continue; // Already handled
                        if (bones == null) bones = skinnedMeshRenderer.bones;
                        int index = Array.IndexOf(bones, pbTransform);
                        if (index < 0) continue; // Not a bone
                        bool hasBoneWeight = false;
                        foreach (var boneWeight in mesh.GetAllBoneWeights())
                            if (boneWeight.boneIndex == index && boneWeight.weight > 0) {
                                hasBoneWeight = true;
                                break;
                            }
                        if (!hasBoneWeight) continue; // Not used
                        pbHasNoReference.Remove(pb);
                        if (pbMapping2.TryGetValue(pb, out var other) && other != rendererTransform) {
                            if (checkedTransforms.Add(pbTransform))
                                Debug.LogWarning($"{pbTransform.name} has been referenced by multiple skinned mesh renderers.", pbTransform);
                            pbHasMultiple.Add(pb);
                            pbMapping2.Remove(pb);
                            continue;
                        }
                        pbMapping2[pb] = rendererTransform;
                    }
                }
            }
        }
        if (pbMapping2.Count <= 0 && pbHasMultiple.Count <= 0 && pbHasNoReference.Count <= 0) return;
        Undo.IncrementCurrentGroup();
        var undoGroup = Undo.GetCurrentGroup();
        var pbTemp = new List<VRCPhysBoneBase>();
        foreach (var kv in pbMapping2)
            CheckAndMovePhysBone(kv.Key, kv.Value, pbTemp);
        if (pbHasMultiple.Count > 0 || pbHasNoReference.Count > 0) {
            var transform = rootTransform.Find("Phys Bones");
            if (transform == null) {
                var newRoot = new GameObject("Phys Bones");
                transform = newRoot.transform;
                transform.SetParent(rootTransform, false);
                Undo.RegisterCreatedObjectUndo(newRoot, "Create new root for phys bones");
            }
            foreach (var pb in pbHasMultiple)
                CheckAndMovePhysBone(pb, transform, pbTemp);
            foreach (var pb in pbHasNoReference)
                CheckAndMovePhysBone(pb, transform, pbTemp);
        }
        Undo.SetCurrentGroupName("Move Phys Bones");
        Undo.CollapseUndoOperations(undoGroup);
    }

    static void CheckAndMovePhysBone(VRCPhysBoneBase pb, Transform parent, List<VRCPhysBoneBase> pbTemp) {
        var pbTransform = pb.rootTransform;
        if (pbTransform == null) pb.rootTransform = pbTransform = pb.transform;
        if (pb.transform.parent == parent) {
            pb.GetComponents(pbTemp);
            if (pbTemp.Count == 1 || pbTemp[0] == pb) return;
        }
        MoveComponentToNewGameObject(pb, $"PB.{pbTransform.name}", parent);
    }

    static void MoveComponentToNewGameObject(Component srcComponent, string gameObjectName, Transform parent, bool undoable = true) {
        var newContainer = new GameObject(GameObjectUtility.GetUniqueNameForSibling(parent, gameObjectName));
        newContainer.transform.SetParent(parent, false);
        if (undoable) Undo.RegisterCreatedObjectUndo(newContainer, $"Craete container for {srcComponent.GetType().Name}");
        MoveComponent(ref srcComponent, newContainer, undoable);
    }

    static void MoveComponent<T>(ref T source, GameObject destGameObject, bool undoable = true) where T : Component {
        if (source == null) {
            Debug.LogWarning("Source component is null.", destGameObject);
            return;
        }
        if (source.gameObject == destGameObject) {
            Debug.LogWarning("Source and destination are the same.", destGameObject);
            return;
        }
        var sourceType = source.GetType();
        if (Attribute.IsDefined(sourceType, typeof(DisallowMultipleComponent), true) &&
            destGameObject.TryGetComponent(sourceType, out var existing) && existing != source) {
            Debug.LogWarning($"Component {sourceType.Name} already exists on {destGameObject.name}.", destGameObject);
            return;
        }
        var newComponent = undoable ?
            Undo.AddComponent(destGameObject, sourceType) :
            destGameObject.AddComponent(sourceType);
        EditorUtility.CopySerialized(source, newComponent);
        if (undoable)
            Undo.DestroyObjectImmediate(source);
        else if (Application.isPlaying)
            Destroy(source);
        else
            DestroyImmediate(source);
        source = newComponent as T;
    }

    [MenuItem("Tools/JLChnToZ/Remove Unused PhysBone Colliders")]
    static void RemoveUnusedColliders() {
        var refCount = new Dictionary<VRCPhysBoneColliderBase, int>();
        foreach (var pbc in Selection.GetFiltered<VRCPhysBoneColliderBase>(SelectionMode.Deep))
            refCount[pbc] = 0;
        foreach (var pb in FindObjectsByType<VRCPhysBoneBase>(FindObjectsSortMode.None))
            foreach (var pbc in pb.colliders)
                if (pbc != null && refCount.TryGetValue(pbc, out var count))
                    refCount[pbc] = count + 1;
        foreach (var kv in refCount) {
            if (kv.Value > 0) continue;
            Undo.DestroyObjectImmediate(kv.Key);
        }
        Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        Undo.SetCurrentGroupName("Remove Unused PhysBone Colliders");
    }
}
#else
public static class VRCPhysBoneHelperX {
    public static void MovePhysBones() {
        Debug.LogWarning("VRC SDK3 for Avatars is not installed.");
    }
}
#endif
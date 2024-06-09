using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityObject = UnityEngine.Object;

public class BoneRemapper : EditorWindow {
    [MenuItem("Tools/JLChnToZ/Bone Remapper")]
    public static void GetWindow() {
        EditorWindow.GetWindow(typeof(BoneRemapper));
    }

    static readonly Type skinnedMeshRendererType = typeof(SkinnedMeshRenderer);

    SkinnedMeshRenderer srcRenderer, destRenderer;
    
    void OnGUI() {
        EditorGUILayout.PrefixLabel("Source");
        srcRenderer = EditorGUILayout.ObjectField(srcRenderer, skinnedMeshRendererType, true) as SkinnedMeshRenderer;
        EditorGUILayout.PrefixLabel("Destination");
        destRenderer = EditorGUILayout.ObjectField(destRenderer, skinnedMeshRendererType, true) as SkinnedMeshRenderer;
        if (GUILayout.Button("Reconstruct") && srcRenderer != null && destRenderer != null) {
            ReconstructBones(srcRenderer, destRenderer);
            EditorUtility.DisplayDialog("Bone Remapper", "Mesh has been successfully copied to destination with different bone hierachy.", "OK");
        }
        EditorGUILayout.HelpBox("Make sure the root bone is assigned in destination skinned mesh renderer.", MessageType.Info);
    }

    static void ReconstructBones(SkinnedMeshRenderer src, SkinnedMeshRenderer dest) {
        var srcBones = src.bones;
        var mapping = new Dictionary<Transform, int>();
        for (int i = 0; i < srcBones.Length; i++) mapping[srcBones[i]] = i;
        var destBones = new Transform[srcBones.Length];
        ConstructIndexList(destBones, mapping, src.rootBone, dest.rootBone);
        dest.sharedMesh = src.sharedMesh;
        dest.bones = destBones;
    }

    static void ConstructIndexList(
        Transform[] destBones,
        Dictionary<Transform, int> mapping,
        Transform src,
        Transform dest
    ) {
        if (mapping.TryGetValue(src, out int i))
            destBones[i] = dest;
        foreach (Transform srcChild in src) {
            var destChild = dest.Find(srcChild.name);
            if (destChild == null) {
                int siblingIndex = srcChild.GetSiblingIndex();
                if (siblingIndex < dest.childCount) destChild = dest.GetChild(siblingIndex);
            }
            if (destChild != null)
                ConstructIndexList(destBones, mapping, srcChild, destChild);
        }
    }
}

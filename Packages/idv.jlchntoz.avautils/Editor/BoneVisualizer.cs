using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

[ExecuteInEditMode]
public class BoneVisualizer : EditorWindow {
    static HashSet<Transform> humanoidBones = new HashSet<Transform>();
    static HashSet<Transform> skinnedMeshBones = new HashSet<Transform>();
    static Queue<Transform> walker = new Queue<Transform>();
    static HashSet<Transform> commonBones = new HashSet<Transform>();
    GameObject objectToDisplay;
    List<Component> activeComponents = new List<Component>();
    bool[] componentEnabled;
    float boneSize = 0.06F;
    Color avatarBoneColor = new Color(0, 1F, 0.75F);
    Color skinnedMeshBoneColor = new Color(0, 0.5F, 1F);
    ReorderableList activeComponentsList;

    [MenuItem("Tools/JLChnToZ/Bone Visualizer")]
    static void ShowWindow() {
        var window = GetWindow<BoneVisualizer>();
        window.titleContent = new GUIContent("Bone Visualizer");
        window.Show();
    }

    void OnEnable() {
        activeComponentsList = new ReorderableList(activeComponents, typeof(Component), false, false, false, false) {
            drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Active Components"),
            drawElementCallback = (rect, index, isActive, isFocused) => {
                var component = activeComponents[index];
                var enabled = componentEnabled[index];
                EditorGUI.BeginChangeCheck();
                enabled = EditorGUI.ToggleLeft(rect, EditorGUIUtility.ObjectContent(component, component.GetType()), enabled);
                if (EditorGUI.EndChangeCheck()) componentEnabled[index] = enabled;
            },
        };
        SceneView.duringSceneGui += OnSceneGUI;
    }

    void OnDisable() {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    void OnGUI() {
        bool shouldRefresh = false;
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.BeginHorizontal();
        objectToDisplay = (GameObject)EditorGUILayout.ObjectField("Object To Display", objectToDisplay, typeof(GameObject), true);
        if (EditorGUI.EndChangeCheck()) shouldRefresh = true;
        if (GUILayout.Button("Refresh", GUILayout.ExpandWidth(false))) shouldRefresh = true;
        if (shouldRefresh) {
            activeComponents.Clear();
            if (objectToDisplay != null) {
                activeComponents.AddRange(objectToDisplay.GetComponentsInChildren<Animator>(true));
                activeComponents.AddRange(objectToDisplay.GetComponentsInChildren<SkinnedMeshRenderer>(true));
            }
            componentEnabled = new bool[activeComponents.Count];
            for (int i = 0; i < componentEnabled.Length; i++) {
                if (activeComponents[i] == null) continue;
                if (activeComponents[i] is Animator animator)
                    componentEnabled[i] = animator.isActiveAndEnabled;
                else if (activeComponents[i] is SkinnedMeshRenderer skinnedMeshRenderer)
                    componentEnabled[i] = skinnedMeshRenderer.enabled && skinnedMeshRenderer.gameObject.activeInHierarchy;
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();
        activeComponentsList.DoLayoutList();
        EditorGUILayout.Space();
        boneSize = EditorGUILayout.Slider("Bone Size", boneSize, 0, 1);
        avatarBoneColor = EditorGUILayout.ColorField("Avatar Bone Color", avatarBoneColor);
        skinnedMeshBoneColor = EditorGUILayout.ColorField("Skinned Mesh Bone Color", skinnedMeshBoneColor);
    }

    void OnSceneGUI(SceneView sceneView) {
        if (Event.current.type != EventType.Repaint) return;
        DrawBonesGizmos();
    }

    void DrawBonesGizmos() {
        humanoidBones.Clear();
        skinnedMeshBones.Clear();
        commonBones.Clear();
        AddBone(objectToDisplay.transform);
        for (int i = 0; i < activeComponents.Count; i++) {
            var component = activeComponents[i];
            if (component == null) continue;
            if (!componentEnabled[i]) continue;
            if (component is Animator animator) {
                for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++) {
                    var bone = animator.GetBoneTransform(b);
                    if (bone == null) continue;
                    if (humanoidBones.Add(bone)) AddBone(bone);
                    var parent = bone.parent;
                    if (!humanoidBones.Contains(parent)) parent = null;
                    DrawBoneGizmos(parent, bone, avatarBoneColor, avatarBoneColor);
                }
            } else if (component is SkinnedMeshRenderer skinnedMeshRenderer) {
                AddBone(skinnedMeshRenderer.rootBone);
                var bones = skinnedMeshRenderer.bones;
                if (bones == null) continue;
                foreach (var bone in bones)
                    if (bone != null && skinnedMeshBones.Add(bone))
                        AddBone(bone);
            }
        }
        walker.Clear();
        foreach (var bone in commonBones) walker.Enqueue(bone);
        while (walker.Count > 0) {
            var current = walker.Dequeue();
            if (current == null) continue;
            if (humanoidBones.Contains(current)) {
                var parent = current.parent;
                Color parentColor = default;
                if (humanoidBones.Contains(parent)) parentColor = avatarBoneColor;
                else if (skinnedMeshBones.Contains(parent)) parentColor = skinnedMeshBoneColor;
                else parent = null;
                DrawBoneGizmos(parent, current, avatarBoneColor, parentColor);
            } else if (skinnedMeshBones.Contains(current)) {
                var parent = current.parent;
                Color parentColor = default;
                if (humanoidBones.Contains(parent)) parentColor = avatarBoneColor;
                else if (skinnedMeshBones.Contains(parent)) parentColor = skinnedMeshBoneColor;
                else parent = null;
                DrawBoneGizmos(parent, current, skinnedMeshBoneColor, parentColor);
            }
            foreach (Transform child in current) walker.Enqueue(child);
        }
    }

    void AddBone(Transform transform) {
        bool isParent = false;
        foreach (var commonBone in commonBones)
            if (transform.IsChildOf(commonBone)) {
                isParent = true;
                break;
            } else if (commonBone.IsChildOf(transform)) {
                commonBones.Remove(commonBone);
                commonBones.Add(transform);
                break;
            }
        if (!isParent) commonBones.Add(transform);
    }

    void DrawBoneGizmos(Transform from, Transform to, Color color, Color parentColor) {
        Handles.color = color;
        var toPos = to.position;
        float scale = GetBoneScale(from, to);
        Handles.DrawWireDisc(toPos, to.up, scale);
        Handles.DrawWireDisc(toPos, to.right, scale);
        Handles.DrawWireDisc(toPos, to.forward, scale);
        if (from == null) return;
        Handles.color = parentColor;
        var fromPos = from.position;
        var parent = from.parent;
        if (!humanoidBones.Contains(parent) && !skinnedMeshBones.Contains(parent)) parent = null;
        scale = GetBoneScale(parent, from);
        var direction = (toPos - fromPos).normalized;
        var left = Vector3.Cross(direction, Vector3.up).normalized;
        var offset = left * scale;
        Handles.DrawLine(fromPos + offset, toPos);
        Handles.DrawLine(fromPos - offset, toPos);
        var up = Vector3.Cross(direction, left).normalized;
        offset = up * scale;
        Handles.DrawLine(fromPos + offset, toPos);
        Handles.DrawLine(fromPos - offset, toPos);
    }

    float GetBoneScale(Transform from, Transform to) =>
        boneSize * to.lossyScale.magnitude * Mathf.Max(0.01F, from != null ? from.TransformVector(to.localPosition).magnitude : 0.25F);
}

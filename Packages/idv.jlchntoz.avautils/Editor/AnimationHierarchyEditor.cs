#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

using UnityObject = UnityEngine.Object;

public class AnimationHierarchyEditor : EditorWindow {
    const int columnWidth = 300;
    static GUIStyle lockIcon;
    Animator animatorObject;
    readonly HashSet<RuntimeAnimatorController> animatorControllers = new HashSet<RuntimeAnimatorController>();
    readonly Dictionary<Motion, Motion> motions = new Dictionary<Motion, Motion>();
    readonly Dictionary<string, HashSet<EditorCurveBinding>> paths = new Dictionary<string, HashSet<EditorCurveBinding>>(StringComparer.Ordinal);
    readonly Dictionary<string, bool> state = new Dictionary<string, bool>();
    readonly Dictionary<string, GameObject> objectCache = new Dictionary<string, GameObject>();
    readonly Dictionary<string, string> tempPathOverrides = new Dictionary<string, string>();
    readonly Dictionary<Type, int> tempTypes = new Dictionary<Type, int>();
    Motion[] motionsArray;
    string[] pathsArray;
    Vector2 scrollPos, scrollPos2;
    GUIContent tempContent;
    bool locked, onlyShowMissing, autoResolveMode, cloneOnModify;
    string sOriginalRoot = "Root";
    string sNewRoot = "SomeNewObject/Root";

    [MenuItem("Tools/JLChnToZ/Animation Hierarchy Editor")]
    static void ShowWindow() => GetWindow<AnimationHierarchyEditor>();

    void OnEnable() {
        OnSelectionChange();
        titleContent = new GUIContent(EditorGUIUtility.IconContent("AnimationClip Icon")) {
            text = "Animation Hierarchy Editor",
        };
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
    }

    void OnDisable() {
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
    }

    void OnSelectionChange() {
        if (locked) return;
        objectCache.Clear();
        motions.Clear();
        animatorControllers.Clear();
        Animator defaultAnimator = null;
        foreach (var obj in Selection.gameObjects) {
            var animator = obj.GetComponentInChildren<Animator>(true);
            if (animator == null) continue;
            if (defaultAnimator == null) defaultAnimator = animator;
            AddClips(animator);
            break;
        }
        foreach (var obj in Selection.objects) {
            if (obj is Animator animator)
                AddClips(animator);
            else if (obj is AnimatorController controller)
                AddClips(controller);
            else if (obj is AnimatorOverrideController overrideController)
                AddClips(overrideController);
            else if (obj is Motion motion)
                AddClips(motion);
        }
        if (animatorObject == null) animatorObject = defaultAnimator;
        if (motions.Count > 0) FillModel();
        Repaint();
    }

    void AddClips(Animator animator) {
        if (animator == null) return;
        var baseController = animator.runtimeAnimatorController;
        if (baseController == null) return;
        if (animatorObject == null) animatorObject = animator;
        if (baseController is AnimatorController controller)
            AddClips(controller);
        else if (baseController is AnimatorOverrideController overrideController)
            AddClips(overrideController);
    }

    void AddClips(AnimatorOverrideController overrideController) {
        if (overrideController == null) return;
        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        while (overrideController != null) {
            animatorControllers.Add(overrideController);
            overrideController.GetOverrides(overrides);
            foreach (var pair in overrides) AddClips(pair.Value);
            var baseController = overrideController.runtimeAnimatorController;
            if (baseController is AnimatorOverrideController chainedOverride) {
                overrideController = chainedOverride;
                continue;
            } else if (baseController is AnimatorController controller)
                AddClips(controller);
            break;
        };
    }

    void AddClips(AnimatorController animatorController) {
        if (animatorController == null) return;
        animatorControllers.Add(animatorController);
        foreach (var layer in animatorController.layers)
            foreach (var state in layer.stateMachine.states)
                AddClips(state.state.motion);
    }

    void AddClips(Motion motion) {
        if (motion == null) return;
        var stack = new Stack<Motion>();
        stack.Push(motion);
        while (stack.Count > 0) {
            motion = stack.Pop();
            if (motion == null || motions.ContainsKey(motion)) continue;
            motions.Add(motion, null);
            if (motion is BlendTree blendTree) {
                foreach (var child in blendTree.children) stack.Push(child.motion);
                continue;
            }
        }
    }

    void OnGUI() {
        if (Event.current.type == EventType.ValidateCommand)
            switch (Event.current.commandName) {
                case "UndoRedoPerformed":
                    FillModel();
                    break;
            }

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
            using (var changed = new EditorGUI.ChangeCheckScope()) {
                animatorObject = EditorGUILayout.ObjectField("Root Animator", animatorObject, typeof(Animator), true, GUILayout.Width(columnWidth * 2)) as Animator;
                if (changed.changed) OnHierarchyChanged();
            }
            GUILayout.FlexibleSpace();
            cloneOnModify = GUILayout.Toggle(cloneOnModify, "Clone on Modify", EditorStyles.toolbarButton);
            if (GUILayout.Button("Save Animation Clips", EditorStyles.toolbarButton)) SaveModifiedAssets();
            if (GUILayout.Button("Save Clones", EditorStyles.toolbarButton)) SaveModifiedAssets(true);
            using (var changed = new EditorGUI.ChangeCheckScope()) {
                autoResolveMode = GUILayout.Toggle(autoResolveMode, "Auto Resolve", EditorStyles.toolbarButton);
                if (changed.changed && autoResolveMode) StartAutoResolve();
            }
        }

        using (var scrollView = new EditorGUILayout.ScrollViewScope(scrollPos, GUILayout.Height(EditorGUIUtility.singleLineHeight * 5))) {
            scrollPos = scrollView.scrollPosition;
            GUILayout.Label("Selected Animation Clips", EditorStyles.boldLabel, GUILayout.Width(columnWidth));
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(true)) {
                using (new EditorGUILayout.VerticalScope(GUILayout.MaxWidth(columnWidth)))
                    foreach (var animationClip in animatorControllers)
                        EditorGUILayout.ObjectField(animationClip, typeof(RuntimeAnimatorController), true);
                using (new EditorGUILayout.VerticalScope(GUILayout.MaxWidth(columnWidth)))
                    foreach (var motion in motions.Keys)
                        if (motion is BlendTree)
                            EditorGUILayout.ObjectField(motion, typeof(BlendTree), true);
                using (new EditorGUILayout.VerticalScope(GUILayout.MaxWidth(columnWidth)))
                    foreach (var motion in motions.Keys)
                        if (motion is AnimationClip)
                            EditorGUILayout.ObjectField(motion, typeof(AnimationClip), true);
            }
        }

        using (new EditorGUI.DisabledScope(motions.Count == 0)) {
            using (new EditorGUILayout.HorizontalScope()) {
                sOriginalRoot = EditorGUILayout.TextField(sOriginalRoot, GUILayout.ExpandWidth(true));
                sNewRoot = EditorGUILayout.TextField(sNewRoot, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Replace Root", EditorStyles.miniButton, GUILayout.ExpandWidth(false))) {
                    Debug.Log("O: " + sOriginalRoot + " N: " + sNewRoot);
                    ReplaceRoot(sOriginalRoot, sNewRoot);
                }
            }
        }

        EditorGUILayout.Space();

        using (new EditorGUILayout.HorizontalScope()) {
            GUILayout.Label("Bindings", EditorStyles.boldLabel, GUILayout.Width(60));
            GUILayout.Label("Animated Object", EditorStyles.boldLabel, GUILayout.Width(columnWidth));
            GUILayout.Label("Reference Path", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(animatorObject == null))
                onlyShowMissing = GUILayout.Toggle(onlyShowMissing, "Only Show Missing", EditorStyles.toggle, GUILayout.ExpandWidth(false));
        }
        using (var scrollView = new EditorGUILayout.ScrollViewScope(scrollPos2)) {
            scrollPos2 = scrollView.scrollPosition;
            if (motions.Count > 0 && pathsArray != null)
                for (int i = 0; i < pathsArray.Length; i++)
                    GUICreatePathItem(pathsArray[i]);
        }
    }

    void GUICreatePathItem(string path) {
        if (!paths.TryGetValue(path, out var properties)) return;
        var gameObject = FindObjectInRoot(path);
        if (onlyShowMissing && gameObject != null) return;

        if (!tempPathOverrides.TryGetValue(path, out var newPath)) newPath = path;

        using (new EditorGUILayout.HorizontalScope()) {
            var color = gameObject != null ? Color.green : Color.red;
            var bgColor = GUI.backgroundColor;
            using (new EditorGUILayout.HorizontalScope(GUILayout.Width(columnWidth + 60), GUILayout.MinHeight(EditorGUIUtility.singleLineHeight))) {
                state.TryGetValue(path, out var expanded);
                using (var changed = new EditorGUI.ChangeCheckScope()) {
                    expanded = EditorGUILayout.Foldout(expanded, properties.Count.ToString(), true);
                    if (changed.changed) state[path] = expanded;
                }
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(columnWidth))) {
                    Type defaultType = null, transformType = null;
                    int customTypeCount = 0;
                    foreach (var entry in properties) {
                        var type = entry.type;
                        if (!tempTypes.TryGetValue(type, out var count)) {
                            if (typeof(Transform).IsAssignableFrom(type))
                                transformType = type;
                            else if (type != typeof(GameObject)) {
                                if (defaultType == null)
                                    defaultType = type;
                                customTypeCount++;
                            }
                        }
                        tempTypes[type] = count + 1;
                    }
                    if (customTypeCount > 1) defaultType = transformType;
                    else if (defaultType == null) defaultType = transformType;
                    if (animatorObject != null) GUI.backgroundColor = color;
                    UnityObject subObject = gameObject;
                    if (defaultType != null && gameObject != null && gameObject.TryGetComponent(defaultType, out var component))
                        subObject = component;
                    using (new EditorGUI.DisabledScope(animatorObject == null))
                    using (var changed = new EditorGUI.ChangeCheckScope()) {
                        var newSubObject = EditorGUILayout.ObjectField(
                            subObject,
                            (subObject != null ? subObject.GetType() : null) ?? defaultType ?? typeof(GameObject),
                            true, GUILayout.ExpandWidth(true)
                        );
                        if (changed.changed) {
                            if (newSubObject is GameObject newGameObject) {}
                            else if (newSubObject is Component newComponent) newGameObject = newComponent.gameObject;
                            else newGameObject = null;
                            if (newGameObject != null) UpdatePath(path, ChildPath(newGameObject));
                        }
                    }
                    if (expanded) {
                        foreach (var kv in tempTypes) {
                            var type = kv.Key;
                            if (tempContent == null) tempContent = new GUIContent();
                            tempContent.image = AssetPreview.GetMiniTypeThumbnail(type);
                            tempContent.text = $"{ObjectNames.NicifyVariableName(type.Name)} ({kv.Value} Bindings)";
                            if (animatorObject != null) GUI.backgroundColor = gameObject != null && (type == typeof(GameObject) || gameObject.TryGetComponent(type, out var _)) ? Color.green : Color.red;
                            EditorGUILayout.LabelField(tempContent, EditorStyles.objectFieldThumb);
                        }
                        EditorGUILayout.Space();
                    }
                    tempTypes.Clear();
                    GUI.backgroundColor = bgColor;
                }
            }
            newPath = EditorGUILayout.TextField(newPath, GUILayout.ExpandWidth(true));
            if (newPath != path) tempPathOverrides[path] = newPath;
            if (GUILayout.Button("Change", EditorStyles.miniButton, GUILayout.ExpandWidth(false))) {
                UpdatePath(path, newPath);
                tempPathOverrides.Remove(path);
            }
        }
    }

    void OnInspectorUpdate() => Repaint();

    void OnHierarchyChanged() {
        if (!autoResolveMode) {
            objectCache.Clear();
            return;
        }
        var kvps = new KeyValuePair<string, GameObject>[objectCache.Count];
        (objectCache as ICollection<KeyValuePair<string, GameObject>>).CopyTo(kvps, 0);
        foreach (var kvp in kvps) {
            var path = kvp.Key;
            var obj = kvp.Value;
            if (obj == null) {
                objectCache.Remove(path);
                continue;
            }
            var newPath = ChildPath(obj);
            if (UpdatePath(path, newPath)) {
                objectCache.Remove(path);
                objectCache[newPath] = obj;
            }
        }
    }

    void FillModel() {
        paths.Clear();
        foreach (var motion in motions.Keys) {
            if (!(motion is AnimationClip animationClip)) continue;
            FillModelWithCurves(AnimationUtility.GetCurveBindings(animationClip));
            FillModelWithCurves(AnimationUtility.GetObjectReferenceCurveBindings(animationClip));
        }
        if (pathsArray == null || pathsArray.Length != paths.Count) pathsArray = new string[paths.Count];
        paths.Keys.CopyTo(pathsArray, 0);
    }

    void StartAutoResolve() {
        objectCache.Clear();
        foreach (var path in paths.Keys) FindObjectInRoot(path);
    }

    void FillModelWithCurves(EditorCurveBinding[] curves) {
        foreach (var curveData in curves) {
            var key = curveData.path;
            if (!paths.TryGetValue(key, out var properties)) {
                properties = new HashSet<EditorCurveBinding>();
                paths.Add(key, properties);
            }
            properties.Add(curveData);
        }
    }

    void ReplaceRoot(string oldRoot, string newRoot) {
        var oldRootMatcher = new Regex($"^{Regex.Escape(oldRoot)}", RegexOptions.Compiled);
        UpdatePath(path => oldRootMatcher.Replace(path, newRoot));
    }

    bool UpdatePath(string oldPath, string newPath) {
        if (oldPath == newPath) return true;
        if (paths.ContainsKey(newPath) && !EditorUtility.DisplayDialog(
            "Path already exists",
            $"Path `{newPath}` already exists.\nDo you want to overwrite it?",
            "Yes", "No"
        )) return false;
        UpdatePath(path => path == oldPath ? newPath : null);
        return true;
    }

    void UpdatePath(Func<string, string> converter) {
        EnsureAssetModifiable();
        AssetDatabase.StartAssetEditing();
        if (motionsArray == null || motionsArray.Length != motions.Count)
            motionsArray = new Motion[motions.Count];
        motions.Keys.CopyTo(motionsArray, 0);
        for (int i = 0; i < motionsArray.Length; i++) {
            if (!(motionsArray[i] is AnimationClip animationClip)) continue;
            if (motions.TryGetValue(motionsArray[i], out var motion) && motion != null)
                animationClip = motion as AnimationClip;
            if (animationClip == null) {
                Debug.LogError($"Motion {motionsArray[i]} is not an animation clip!");
                continue;
            }
            if ((cloneOnModify && motion == null) || AssetDatabase.IsForeignAsset(animationClip)) {
                var newClip = Instantiate(animationClip);
                newClip.name = animationClip.name;
                motions[animationClip] = newClip;
                animationClip = newClip;
            }
            Undo.RecordObject(animationClip, "Animation Hierarchy Change");
            var curves = AnimationUtility.GetCurveBindings(animationClip);
            for (int j = 0; j < curves.Length; j++)
                try {
                    var binding = curves[j];
                    var newPath = converter(binding.path);
                    if (string.IsNullOrEmpty(newPath)) continue;
                    UpdateBinding(motionsArray[i] as AnimationClip, animationClip, binding, newPath);
                } finally {
                    EditorUtility.DisplayProgressBar(
                        "Updating Animation Hierarchy",
                        animationClip.name,
                        (i + (float)j / curves.Length) / motions.Count
                    );
                }
        }
        AssetDatabase.StopAssetEditing();
        EditorUtility.ClearProgressBar();
        FillModel();
        Repaint();
    }

    static void UpdateBinding(AnimationClip oldClip, AnimationClip newClip, EditorCurveBinding binding, string newPath) {
        if (newClip == null) newClip = oldClip;
        var editorCurve = AnimationUtility.GetEditorCurve(oldClip, binding);
        if (editorCurve != null) {
            AnimationUtility.SetEditorCurve(newClip, binding, null);
            binding.path = newPath;
            AnimationUtility.SetEditorCurve(newClip, binding, editorCurve);
            return;
        }
        var objRefCurve = AnimationUtility.GetObjectReferenceCurve(oldClip, binding);
        if (objRefCurve != null) {
            AnimationUtility.SetObjectReferenceCurve(newClip, binding, null);
            binding.path = newPath;
            AnimationUtility.SetObjectReferenceCurve(newClip, binding, objRefCurve);
            return;
        }
    }

    void EnsureAssetModifiable() {
        foreach (var kv in motions) {
            if (kv.Value != null) {
                if (AssetDatabase.IsForeignAsset(kv.Value))
                    throw new UnityException($"Animation clip {kv.Value} is not modifiable!");
                continue;
            }
            if (AssetDatabase.IsForeignAsset(kv.Key)) {
                if (!SaveModifiedAssets())
                    throw new UnityException($"Animation clip {kv.Key} is not modifiable!");
                break;
            }
        }
    }

    bool SaveModifiedAssets(bool forceSaveAs = false) {
        var motionRequireToSave = new HashSet<Motion>();
        var motionList = new List<Motion>(motions.Keys);
        foreach (var motion in motionList)
            if (motions.TryGetValue(motion, out var modifiedMotion)) {
                if (forceSaveAs) {
                    modifiedMotion = Instantiate(modifiedMotion == null ? motion : modifiedMotion);
                    motions[motion] = modifiedMotion;
                }
                if (modifiedMotion != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(modifiedMotion)))
                    motionRequireToSave.Add(modifiedMotion);
            }
        UnityObject rootAsset = null;
        var path = EditorUtility.SaveFilePanelInProject(
            "Save modified animation clip",
            "ModifiedAnimationClip",
            "asset",
            "Save modified animation clips",
            AssetDatabase.GetAssetPath(animatorObject) ?? ""
        );
        if (string.IsNullOrEmpty(path)) return false;
        foreach (var clip in motionRequireToSave) {
            if (rootAsset == null) {
                rootAsset = clip;
                AssetDatabase.CreateAsset(clip, path);
            } else
                AssetDatabase.AddObjectToAsset(clip, rootAsset);
        }
        List<KeyValuePair<AnimationClip, AnimationClip>> overrides = null;
        var stack = new Stack<Motion>();
        foreach (var controller in animatorControllers) {
            if (controller is AnimatorController animatorController) {
                foreach (var layer in animatorController.layers)
                    foreach (var state in layer.stateMachine.states) {
                        var animState = state.state;
                        var motion = animState.motion;
                        if (motion == null) continue;
                        if (motions.TryGetValue(motion, out var modifiedMotion)) {
                            animState.motion = modifiedMotion;
                            EditorUtility.SetDirty(animState);
                        }
                        stack.Push(motion);
                    }
            } else if (controller is AnimatorOverrideController animatorOverrideController) {
                if (overrides == null) overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                animatorOverrideController.GetOverrides(overrides);
                for (int i = 0, count = overrides.Count; i < count; i++) {
                    var pair = overrides[i];
                    if (motions.TryGetValue(pair.Value, out var modifiedClip))
                        overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(pair.Key, modifiedClip as AnimationClip);
                }
                animatorOverrideController.ApplyOverrides(overrides);
            }
            EditorUtility.SetDirty(controller);
        }
        while (stack.Count > 0) {
            var motion = stack.Pop();
            if (motion is BlendTree blendTree) {
                var children = blendTree.children;
                bool hasModified = false;
                for (int i = 0, count = children.Length; i < count; i++) {
                    var child = children[i];
                    if (motions.TryGetValue(child.motion, out var modifiedMotion)) {
                        child.motion = modifiedMotion;
                        children[i] = child;
                        hasModified = true;
                    }
                    stack.Push(child.motion);
                }
                if (hasModified) {
                    blendTree.children = children;
                    EditorUtility.SetDirty(blendTree);
                }
            }
        }
        AssetDatabase.SaveAssets();
        motionList.Clear();
        foreach (var kv in motions) motionList.Add(kv.Value != null ? kv.Value : kv.Key);
        motions.Clear();
        foreach (var clip in motionList) motions[clip] = null;
        return true;
    }

    GameObject FindObjectInRoot(string path) {
        if (animatorObject == null) return null;
        if (!objectCache.TryGetValue(path, out var obj)) {
            var child = animatorObject.transform.Find(path);
            if (child != null) obj = child.gameObject;
            objectCache.Add(path, obj);
        }
        return obj;
    }

    string ChildPath(GameObject obj) {
        if (animatorObject == null)
            throw new UnityException("Please assign Referenced Animator (Root) first!");
        var stack = new Stack<Transform>();
        var rootTransform = animatorObject.transform;
        for (var current = obj.transform; current != rootTransform; current = current.parent) {
            if (current == null)
                throw new UnityException($"Object must belong to {animatorObject}!");
            stack.Push(current);
        }
        var names = new string[stack.Count];
        for (int i = 0; i < names.Length; i++)
            names[i] = stack.Pop().name;
        return string.Join("/", names);
    }

    void ShowButton(Rect rect) {
        if (lockIcon == null) lockIcon = GUI.skin.FindStyle("IN LockButton");
        using (var changed = new EditorGUI.ChangeCheckScope()) {
            locked = GUI.Toggle(rect, locked, GUIContent.none, lockIcon);
            if (changed.changed && !locked) OnSelectionChange();
        }
    }
}
#endif
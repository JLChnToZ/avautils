using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEditor;
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.CommonUtils {
    public class GhostedSubAssetFinder : EditorWindow {
        const string MENU_PATH = "Assets/Find Ghosted Sub Assets";
        static readonly GUIContent tempContent = new();
        static GUILayoutOption[] noExpandWidth, expandWidth;
        readonly HashSet<Dependency> listed = new();
        readonly Dictionary<UnityObject, bool> expanded = new();
        [NonSerialized] Dependency[] dependencies;
        [SerializeField] string mainAssetPath;
        [SerializeField] Vector2 scrollPosition;
        [SerializeField] bool skipConfirm;

        [MenuItem(MENU_PATH)]
        static void FindGhostedSubAssetsMenu() {
            var scannedPaths = new HashSet<string>();
            foreach (var asset in Selection.GetFiltered(typeof(UnityObject), SelectionMode.Assets)) {
                var path = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(path) ||
                    !scannedPaths.Add(path) ||
                    path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    continue;
                var window = CreateInstance<GhostedSubAssetFinder>();
                window.mainAssetPath = path;
                window.ShowUtility();
                window.Refresh();
            }
        }

        static Texture2D GetThumbnail(UnityObject asset) {
            var thumbnail = AssetPreview.GetMiniThumbnail(asset);
            if (thumbnail.width == 0)
                thumbnail = AssetPreview.GetMiniTypeThumbnail(asset.GetType());
            return thumbnail;
        }

        static GUIContent ImageContent(string imageId, string tooltip = "") =>
            TempContent(image: EditorGUIUtility.IconContent(imageId).image, tooltip: tooltip);

        static GUIContent TempContent(string text = "", Texture image = null, string tooltip = "") {
            tempContent.text = text;
            tempContent.image = image;
            tempContent.tooltip = tooltip;
            return tempContent;
        }

        [MenuItem(MENU_PATH, true)]
        static bool FindGhostedSubAssetsMenuValidate() {
            foreach (var asset in Selection.GetFiltered(typeof(UnityObject), SelectionMode.Assets)) {
                var path = AssetDatabase.GetAssetPath(asset);
                if (!string.IsNullOrEmpty(path) &&
                    !path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        void OnEnable() {
            noExpandWidth ??= new [] {
                GUILayout.ExpandWidth(false),
                GUILayout.Height(EditorGUIUtility.singleLineHeight),
            };
            expandWidth ??= new [] {
                GUILayout.ExpandWidth(true),
                GUILayout.Height(EditorGUIUtility.singleLineHeight),
            };
            if (!string.IsNullOrEmpty(mainAssetPath) && dependencies == null)
                Refresh();
        }

        void Refresh() {
            dependencies = Dependency.Scan(mainAssetPath);
            if (dependencies == null) {
                Close();
                return;
            }
            var title = titleContent;
            var depds = dependencies[0];
            titleContent.text = $"{ObjectNames.GetInspectorTitle(depds.asset)} - Ghosted Sub Assets Finder";
            titleContent = title;
        }

        void OnGUI() {
            bool shouldRefresh = false;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, Array.Empty<GUILayoutOption>())) {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, noExpandWidth)) shouldRefresh = true;
                GUILayout.Label($"Dependency Groups: {dependencies?.Length ?? 0}", EditorStyles.label, noExpandWidth);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Clean All", EditorStyles.toolbarButton, noExpandWidth))
                    shouldRefresh |= ConfirmAndCleanAll();
                skipConfirm = GUILayout.Toggle(skipConfirm, "Skip Confirmations", EditorStyles.toolbarButton, noExpandWidth);
            }
            using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPosition, Array.Empty<GUILayoutOption>())) {
                scrollPosition = scroll.scrollPosition;
                if (dependencies != null) {
                    foreach (var root in dependencies) {
                        if (!DrawEntry(root, root.Count > 0, 0, ref shouldRefresh)) continue;
                        for (var deepDepds = root.DeepDependencies; deepDepds.MoveNext(); )
                            if (!DrawEntry(
                                deepDepds.Current,
                                deepDepds.IsEnteringChildren,
                                deepDepds.Depth * 16,
                                ref shouldRefresh
                            )) deepDepds.CancelYieldChildrenOnNext();
                    }
                }
                GUILayout.FlexibleSpace();
            }
            if (shouldRefresh) {
                AssetDatabase.SaveAssets();
                Refresh();
            }
        }

        bool DrawEntry(Dependency depd, bool expandable, float indentOffset, ref bool shouldRefresh) {
            bool isHidden = (depd.asset.hideFlags & (HideFlags.HideInHierarchy | HideFlags.HideInInspector)) != HideFlags.None;
            bool isExpanded = false;
            using (new EditorGUILayout.HorizontalScope(Array.Empty<GUILayoutOption>())) {
                TempContent(ObjectNames.GetInspectorTitle(depd.asset), GetThumbnail(depd.asset), depd.asset.name);
                var color = GUI.color;
                if (isHidden) GUI.color = color * 0.75F;
                if (expandable) {
                    expanded.TryGetValue(depd.asset, out isExpanded);
                    GUILayout.Space(indentOffset);
                    using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
                        isExpanded = GUI.Toggle(GUILayoutUtility.GetRect(
                            0, position.width - indentOffset,
                            EditorGUIUtility.singleLineHeight,
                            EditorGUIUtility.singleLineHeight,
                            EditorStyles.foldout
                        ), isExpanded, tempContent, EditorStyles.foldout);
                        if (changeCheck.changed) expanded[depd.asset] = isExpanded;
                    }
                } else {
                    indentOffset += 13;
                    GUILayout.Space(indentOffset);
                    GUI.Label(GUILayoutUtility.GetRect(
                        0, position.width - indentOffset,
                        EditorGUIUtility.singleLineHeight,
                        EditorGUIUtility.singleLineHeight,
                        EditorStyles.label
                    ), tempContent, EditorStyles.label);
                }
                GUI.color = color;
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(ImageContent("scenepicking_pickable_hover", "Locate"), EditorStyles.iconButton, noExpandWidth))
                    EditorGUIUtility.PingObject(depd.asset);
                if (GUILayout.Button(ImageContent("Selectable Icon", "Select"), EditorStyles.iconButton, noExpandWidth))
                    Selection.activeObject = depd.asset;
                if (GUILayout.Button(ImageContent("TreeEditor.Trash", "Delete"), EditorStyles.iconButton, noExpandWidth))
                    shouldRefresh |= Event.current.shift ? ConfirmAndCleanDeep(depd) : ConfirmAndCleanSingle(depd);
            }
            return expandable && isExpanded;
        }

        bool ConfirmAndCleanSingle(Dependency depd) => (skipConfirm || EditorUtility.DisplayDialog(
            "Delete Sub Asset",
            $"Are you sure you want to delete {depd.asset.name}?",
            "Yes", "No"
        )) && Cleanup(depd, false);

        bool ConfirmAndCleanDeep(Dependency depd) => (skipConfirm || EditorUtility.DisplayDialog(
            "Delete All Descendants",
            $"Are you sure you want to delete all descendants of {depd.asset.name}?",
            "Yes", "No"
        )) && Cleanup(depd, true);

        bool ConfirmAndCleanAll() => (skipConfirm || EditorUtility.DisplayDialog(
            "Delete All Ghosted Sub Assets",
            "Are you sure you want to delete all ghosted sub assets?",
            "Yes", "No"
        )) && Cleanup(null, true);

        bool Cleanup(Dependency depd, bool deepClean) {
            if (deepClean) {
                listed.Clear();
                if (dependencies != null) {
                    for (int i = depd == null ? 1 : 0; i < dependencies.Length; i++) {
                        var currentDepd = dependencies[i];
                        if (currentDepd.Equals(depd)) continue;
                        listed.Add(currentDepd);
                        for (var deepDepds = currentDepd.DeepDependencies; deepDepds.MoveNext(); ) {
                            var otherDepd = deepDepds.Current;
                            if (!otherDepd.Equals(depd) && !listed.Add(otherDepd))
                                deepDepds.CancelYieldChildrenOnNext();
                        }
                    }
                    if (depd != null) {
                        for (var deepDepds = depd.DeepDependencies; deepDepds.MoveNext(); ) {
                            var deepDepd = deepDepds.Current;
                            if (listed.Add(deepDepd))
                                AssetDatabase.RemoveObjectFromAsset(deepDepd.asset);
                        }
                    } else {
                        listed.ExceptWith(dependencies[0].DeepDependencies);
                        foreach (var deepDepd in listed)
                            AssetDatabase.RemoveObjectFromAsset(deepDepd.asset);
                    }
                    listed.Clear();
                }
            }
            if (depd != null) AssetDatabase.RemoveObjectFromAsset(depd.asset);
            return true;
        }

        class Dependency : ICollection<Dependency>, IReadOnlyCollection<Dependency>, IEquatable<Dependency> {
            public readonly UnityObject asset;
            HashSet<Dependency> dependencies;

            public DeepEnumerator DeepDependencies => new(this);

            public int Count => dependencies != null ? dependencies.Count : 0;

            bool ICollection<Dependency>.IsReadOnly => true;

            public static Dependency[] Scan(string assetPath) {
                if (assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    return Array.Empty<Dependency>();
                var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                if (assets == null || assets.Length == 0)
                    return Array.Empty<Dependency>();
                var assetSources = new HashSet<UnityObject>(assets);
                Dependency mainDepd = null;
                var mapping = new Dictionary<UnityObject, Dependency>();
                foreach (var asset in assetSources) {
                    if (!asset) continue;
                    var depd = new Dependency(asset);
                    mapping[asset] = depd;
                    if (mainDepd == null && AssetDatabase.IsMainAsset(asset))
                        mainDepd = depd;
                }
                var roots = new HashSet<Dependency>(mapping.Values);
                if (mainDepd != null) {
                    mainDepd.GatherDepdencies(mapping, roots);
                    assetSources.Remove(mainDepd.asset);
                    roots.Remove(mainDepd);
                }
                foreach (var asset in assetSources)
                    if (asset && mapping.TryGetValue(asset, out var depd))
                        depd.GatherDepdencies(mapping, roots);
                var result = new List<Dependency>();
                if (mainDepd != null) result.Add(mainDepd);
                result.AddRange(roots);
                return result.ToArray();
            }

            Dependency(UnityObject asset) => this.asset = asset;

            void GatherDepdencies(Dictionary<UnityObject, Dependency> mapping, HashSet<Dependency> roots) {
                using var so = new SerializedObject(asset);
                for (var sp = so.GetIterator(); sp.Next(true); ) {
                    if (sp.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var other = sp.objectReferenceValue;
                    if (other && other != asset && mapping.TryGetValue(other, out var otherDepd)) {
                        dependencies ??= new HashSet<Dependency>();
                        dependencies.Add(otherDepd);
                        roots.Remove(otherDepd);
                    }
                }
            }

            public bool Contains(Dependency item) => dependencies != null && dependencies.Contains(item);

            public void CopyTo(Dependency[] array, int arrayIndex) => dependencies?.CopyTo(array, arrayIndex);

            public Enumerator GetEnumerator() => new(dependencies);

            IEnumerator<Dependency> IEnumerable<Dependency>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            void ICollection<Dependency>.Add(Dependency item) => throw new NotSupportedException();

            void ICollection<Dependency>.Clear() => throw new NotSupportedException();

            bool ICollection<Dependency>.Remove(Dependency item) => throw new NotSupportedException();

            public bool Equals(Dependency other) => other != null && asset == other.asset;

            public override bool Equals(object obj) => Equals(obj as Dependency);

            public override int GetHashCode() => asset ? asset.GetHashCode() : 0;

            public struct DeepEnumerator : IEnumerable<Dependency>, IEnumerator<Dependency> {
                readonly Stack<(Dependency parent, HashSet<Dependency>.Enumerator enumerator)> stack;
                Dependency current;

                public readonly int Depth {
                    get {
                        var depth = stack.Count;
                        return IsEnteringChildren ? depth - 1 : depth;
                    }
                }

                public readonly bool IsEnteringChildren =>
                    stack.TryPeek(out var entry) && entry.parent.Equals(current);

                public readonly Dependency Current => current;

                readonly object IEnumerator.Current => current;

                public DeepEnumerator(Dependency root) {
                    stack = new();
                    current = root;
                    YieldChildrenOnNext();
                }

                public bool MoveNext() {
                    while (stack.TryPop(out var entry)) {
                        current = null;
                        ref var enumerator = ref entry.enumerator;
                        if (!enumerator.MoveNext()) {
                            enumerator.Dispose();
                            continue;
                        }
                        stack.Push(entry);
                        current = enumerator.Current;
                        if (current == null) continue;
                        bool isCircular = false;
                        foreach (var (parent, _) in stack)
                            if (parent.Equals(current)) {
                                isCircular = true;
                                break;
                            }
                        if (isCircular) continue;
                        YieldChildrenOnNext();
                        return true;
                    }
                    return false;
                }

                readonly void YieldChildrenOnNext() {
                    ref var dependencies = ref current.dependencies;
                    if (dependencies != null && dependencies.Count > 0)
                        stack.Push((current, dependencies.GetEnumerator()));
                }

                public readonly bool CancelYieldChildrenOnNext() {
                    if (stack.TryPeek(out var entry) && entry.parent.Equals(current)) {
                        entry.enumerator.Dispose();
                        stack.Pop();
                        return true;
                    }
                    return false;
                }

                readonly void IEnumerator.Reset() {}

                public readonly void Dispose() {
                    while (stack.TryPop(out var entry))
                        entry.enumerator.Dispose();
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public readonly DeepEnumerator GetEnumerator() => this;

                readonly IEnumerator<Dependency> IEnumerable<Dependency>.GetEnumerator() => this;

                readonly IEnumerator IEnumerable.GetEnumerator() => this;
            }

            public struct Enumerator : IEnumerator<Dependency> {
                readonly bool vaild;
                HashSet<Dependency>.Enumerator enumerator;

                public readonly Dependency Current => enumerator.Current;

                readonly object IEnumerator.Current => enumerator.Current;

                public Enumerator(HashSet<Dependency> hashset) {
                    vaild = hashset != null;
                    enumerator = vaild ? hashset.GetEnumerator() : default;
                }

                public bool MoveNext() => vaild && enumerator.MoveNext();

                readonly void IEnumerator.Reset() {}

                public void Dispose() {
                    if (vaild) enumerator.Dispose();
                }
            }
        }
    }
}
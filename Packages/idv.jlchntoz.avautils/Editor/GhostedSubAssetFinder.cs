using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.CommonUtils {
    public class GhostedSubAssetFinder : EditorWindow {
        const string MENU_PATH = "Assets/Find Ghosted Sub Assets";
        static readonly GUIContent tempContent = new();
        static GUILayoutOption[] noExpandWidth, expandWidth;
        readonly HashSet<DependencyNode> listed = new();
        readonly Dictionary<UnityObject, bool> expanded = new();
        [NonSerialized] DependencyNode[] groups;
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
            noExpandWidth ??= new[] {
                GUILayout.ExpandWidth(false),
                GUILayout.Height(EditorGUIUtility.singleLineHeight),
            };
            expandWidth ??= new[] {
                GUILayout.ExpandWidth(true),
                GUILayout.Height(EditorGUIUtility.singleLineHeight),
            };
            if (!string.IsNullOrEmpty(mainAssetPath) && groups == null)
                Refresh();
        }

        void Refresh() {
            groups = DependencyNode.Scan(mainAssetPath);
            if (groups == null) {
                Close();
                return;
            }
            var title = titleContent;
            titleContent.text = $"{ObjectNames.GetInspectorTitle(groups[0].asset)} - Ghosted Sub Assets Finder";
            titleContent = title;
        }

        void OnGUI() {
            bool shouldRefresh = false;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, Array.Empty<GUILayoutOption>())) {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, noExpandWidth)) shouldRefresh = true;
                GUILayout.Label($"Dependency Groups: {groups?.Length ?? 0}", EditorStyles.label, noExpandWidth);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Clean All", EditorStyles.toolbarButton, noExpandWidth))
                    shouldRefresh |= ConfirmAndCleanAll();
                skipConfirm = GUILayout.Toggle(skipConfirm, "Skip Confirmations", EditorStyles.toolbarButton, noExpandWidth);
            }
            using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPosition, Array.Empty<GUILayoutOption>())) {
                scrollPosition = scroll.scrollPosition;
                if (groups != null)
                    foreach (var root in groups)
                        if (DrawEntry(root, !root.IsEmpty, 0, ref shouldRefresh))
                            for (var e = root.GetEnumerator(); e.MoveNext();)
                                if (!DrawEntry(e.Current, e.IsEnteringChildren, e.Depth * 16F, ref shouldRefresh))
                                    e.CancelYieldChildrenOnNext();
                GUILayout.FlexibleSpace();
            }
            if (shouldRefresh) {
                AssetDatabase.SaveAssets();
                Refresh();
            }
        }

        bool DrawEntry(DependencyNode node, bool expandable, float indentOffset, ref bool shouldRefresh) {
            bool isHidden = (node.asset.hideFlags & (HideFlags.HideInHierarchy | HideFlags.HideInInspector)) != HideFlags.None;
            bool isExpanded = false;
            using (new EditorGUILayout.HorizontalScope(Array.Empty<GUILayoutOption>())) {
                TempContent(ObjectNames.GetInspectorTitle(node.asset), GetThumbnail(node.asset), node.asset.name);
                var color = GUI.color;
                if (isHidden) GUI.color = color * 0.75F;
                if (expandable) {
                    expanded.TryGetValue(node.asset, out isExpanded);
                    GUILayout.Space(indentOffset);
                    using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
                        isExpanded = GUI.Toggle(GUILayoutUtility.GetRect(
                            0, position.width - indentOffset,
                            EditorGUIUtility.singleLineHeight,
                            EditorGUIUtility.singleLineHeight,
                            EditorStyles.foldout
                        ), isExpanded, tempContent, EditorStyles.foldout);
                        if (changeCheck.changed) expanded[node.asset] = isExpanded;
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
                    EditorGUIUtility.PingObject(node.asset);
                if (GUILayout.Button(ImageContent("Selectable Icon", "Select"), EditorStyles.iconButton, noExpandWidth))
                    Selection.activeObject = node.asset;
                if (GUILayout.Button(ImageContent("TreeEditor.Trash", "Delete"), EditorStyles.iconButton, noExpandWidth))
                    shouldRefresh |= Event.current.shift ? ConfirmAndCleanDeep(node) : ConfirmAndCleanSingle(node);
            }
            return expandable && isExpanded;
        }

        bool ConfirmAndCleanSingle(DependencyNode node) => (skipConfirm || EditorUtility.DisplayDialog(
            "Delete Sub Asset",
            $"Are you sure you want to delete {node.asset.name}?",
            "Yes", "No"
        )) && Cleanup(node, false);

        bool ConfirmAndCleanDeep(DependencyNode node) => (skipConfirm || EditorUtility.DisplayDialog(
            "Delete All Descendants",
            $"Are you sure you want to delete all descendants of {node.asset.name}?",
            "Yes", "No"
        )) && Cleanup(node, true);

        bool ConfirmAndCleanAll() => (skipConfirm || EditorUtility.DisplayDialog(
            "Delete All Ghosted Sub Assets",
            "Are you sure you want to delete all ghosted sub assets?",
            "Yes", "No"
        )) && Cleanup(null, true);

        bool Cleanup(DependencyNode node, bool deepClean) {
            if (deepClean) {
                listed.Clear();
                if (groups != null) {
                    for (int i = node ? 1 : 0; i < groups.Length; i++) {
                        var currentDepd = groups[i];
                        if (currentDepd.Equals(node)) continue;
                        listed.Add(currentDepd);
                        for (var e = currentDepd.GetEnumerator(); e.MoveNext();) {
                            var otherDepd = e.Current;
                            if (!otherDepd.Equals(node) && !listed.Add(otherDepd))
                                e.CancelYieldChildrenOnNext();
                        }
                    }
                    if (node) {
                        for (var e = node.GetEnumerator(); e.MoveNext();) {
                            var toDelete = e.Current;
                            if (listed.Add(toDelete))
                                AssetDatabase.RemoveObjectFromAsset(toDelete.asset);
                        }
                    } else {
                        listed.Remove(groups[0]);
                        listed.ExceptWith(groups[0]);
                        foreach (var toDelete in listed)
                            AssetDatabase.RemoveObjectFromAsset(toDelete.asset);
                    }
                    listed.Clear();
                }
            }
            if (node) AssetDatabase.RemoveObjectFromAsset(node.asset);
            return true;
        }

        sealed class DependencyNode : IEnumerable<DependencyNode>, IEquatable<DependencyNode> {
            public readonly UnityObject asset;
            DependencyNode[] dependencies = Array.Empty<DependencyNode>();

            public bool IsEmpty => dependencies.Length == 0;

            static bool NotNull(UnityObject asset) => asset;

            static DependencyNode FromMapping(Dictionary<UnityObject, DependencyNode> map, UnityObject asset) {
                if (!map.TryGetValue(asset, out var node)) map[asset] = node = new(asset);
                return node;
            }

            public static DependencyNode[] Scan(string assetPath) {
                if (assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    return Array.Empty<DependencyNode>();
                var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                if (assets == null) return Array.Empty<DependencyNode>();
                var assetSet = new HashSet<UnityObject>(assets.Where(NotNull));
                if (assetSet.Count == 0) return Array.Empty<DependencyNode>();
                var roots = new HashSet<UnityObject>(assetSet);
                var obj2RawDepds = new Dictionary<UnityObject, HashSet<UnityObject>>(assetSet.Count);
                UnityObject mainAsset = null;
                foreach (var asset in assetSet) {
                    if (!mainAsset && AssetDatabase.IsMainAsset(asset))
                        mainAsset = asset;
                    using var so = new SerializedObject(asset);
                    for (var sp = so.GetIterator(); sp.Next(true);) {
                        if (sp.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var other = sp.objectReferenceValue;
                        if (!other || other == asset || !assetSet.Contains(other))
                            continue;
                        if (!obj2RawDepds.TryGetValue(asset, out var rawDepds))
                            obj2RawDepds[asset] = rawDepds = new();
                        rawDepds.Add(other);
                        roots.Remove(other);
                    }
                }
                var obj2Nodes = new Dictionary<UnityObject, DependencyNode>(obj2RawDepds.Count);
                foreach (var asset in assetSet) {
                    var node = FromMapping(obj2Nodes, asset);
                    if (!obj2RawDepds.TryGetValue(asset, out var rawDepds))
                        continue;
                    var dependencies = new DependencyNode[rawDepds.Count];
                    node.dependencies = dependencies;
                    int i = 0;
                    foreach (var other in rawDepds)
                        dependencies[i++] = FromMapping(obj2Nodes, other);
                }
                var result = new List<DependencyNode>(roots.Count);
                if (mainAsset) {
                    roots.Remove(mainAsset);
                    result.Add(obj2Nodes[mainAsset]);
                }
                foreach (var root in roots)
                    if (obj2Nodes.TryGetValue(root, out var node))
                        result.Add(node);
                return result.ToArray();
            }

            DependencyNode(UnityObject asset) => this.asset = asset;

            public Enumerator GetEnumerator() => new(this);

            IEnumerator<DependencyNode> IEnumerable<DependencyNode>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public bool Equals(DependencyNode other) => other != null && asset == other.asset;

            public override bool Equals(object obj) => obj is DependencyNode other && Equals(other);

            public override int GetHashCode() => asset ? asset.GetHashCode() : 0;

            public static bool operator true(DependencyNode node) => node != null && node.asset;

            public static bool operator false(DependencyNode node) => node == null || !node.asset;

            public struct Enumerator : IEnumerator<DependencyNode> {
                readonly Stack<(DependencyNode parent, int index)> stack;
                DependencyNode current;

                public readonly int Depth {
                    get {
                        var depth = stack.Count;
                        return IsEnteringChildren ? depth - 1 : depth;
                    }
                }

                public readonly bool IsEnteringChildren =>
                    stack.TryPeek(out var entry) && entry.parent.Equals(current);

                public readonly DependencyNode Current => current;

                readonly object IEnumerator.Current => current;

                public Enumerator(DependencyNode root) {
                    stack = new();
                    current = root;
                    YieldChildrenOnNext();
                }

                public bool MoveNext() {
                    while (stack.TryPop(out var entry)) {
                        current = default;
                        var dependencies = entry.parent.dependencies;
                        ref var index = ref entry.index;
                        if (++index >= dependencies.Length)
                            continue;
                        stack.Push(entry);
                        if (current = dependencies[index]) {
                            foreach (var (parent, _) in stack)
                                if (parent.Equals(current))
                                    goto SkipCurrent;
                            YieldChildrenOnNext();
                            return true;
                        }
                        SkipCurrent:;
                    }
                    return false;
                }

                readonly void YieldChildrenOnNext() {
                    if (current.dependencies.Length > 0) stack.Push((current, -1));
                }

                public readonly bool CancelYieldChildrenOnNext() {
                    if (stack.TryPeek(out var entry) && entry.parent.Equals(current)) {
                        stack.Pop();
                        return true;
                    }
                    return false;
                }

                readonly void IEnumerator.Reset() { }

                public readonly void Dispose() => stack.Clear();
            }
        }
    }
}
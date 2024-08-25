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
        static readonly HashSet<GhostedSubAssetFinder> openedWindows = new();
        static GUIStyle toolbarLabelStyle;
        static readonly GUIContent tempContent = new();
        static GUILayoutOption[] noExpandWidth, expandWidth;
        readonly HashSet<DependencyNode> listed = new();
        readonly Dictionary<UnityObject, bool> expanded = new();
        [NonSerialized] DependencyNode[] groups;
        [SerializeField] string mainAssetPath;
        [SerializeField] Vector2 scrollPosition;
        [SerializeField] bool skipConfirm;

        public static GhostedSubAssetFinder ShowWindow(UnityObject asset) =>
            ShowWindow(AssetDatabase.GetAssetPath(asset));

        public static GhostedSubAssetFinder ShowWindow(string path) {
            if (string.IsNullOrEmpty(path))
                return null;
            var window = CreateInstance<GhostedSubAssetFinder>();
            window.mainAssetPath = path;
            window.ShowUtility();
            window.Refresh();
            return window;
        }

        [MenuItem(MENU_PATH)]
        static void ShowWindows() {
            var scannedPaths = new HashSet<string>();
            foreach (var asset in Selection.GetFiltered(typeof(UnityObject), SelectionMode.Assets)) {
                var path = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(path) || !scannedPaths.Add(path))
                    continue;
                ShowWindow(path);
            }
        }

        [MenuItem(MENU_PATH, true)]
        static bool ShowWindowsMenuValidate() =>
            Selection.GetFiltered(typeof(UnityObject), SelectionMode.Assets).Length > 0;

        static Texture2D GetThumbnail(UnityObject asset) {
            var thumbnail = AssetPreview.GetMiniThumbnail(asset);
            if (thumbnail.width == 0)
                thumbnail = AssetPreview.GetMiniTypeThumbnail(asset.GetType());
            return thumbnail;
        }

        static GUIContent TempImageContent(string imageId, string tooltip = "") =>
            TempContent(image: EditorGUIUtility.IconContent(imageId).image, tooltip: tooltip);

        static GUIContent TempContent(string text = "", Texture image = null, string tooltip = "") {
            tempContent.text = text;
            tempContent.image = image;
            tempContent.tooltip = tooltip;
            return tempContent;
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
            openedWindows.Add(this);
        }

        void OnDisable() {
            openedWindows.Remove(this);
        }

        void Refresh() {
            if (groups != null && groups.Length > 0 && groups[0].asset) {
                var newAssetPath = AssetDatabase.GetAssetPath(groups[0].asset);
                if (!string.IsNullOrEmpty(newAssetPath) && mainAssetPath != newAssetPath)
                    mainAssetPath = newAssetPath;
            }
            groups = DependencyNode.Scan(mainAssetPath);
            if (groups.Length == 0) {
                Close();
                return;
            }
            var title = titleContent;
            titleContent.text = $"{ObjectNames.GetInspectorTitle(groups[0].asset)} - Ghosted Sub Assets Finder";
            titleContent.image = AssetDatabase.GetCachedIcon(mainAssetPath);
            titleContent = title;
        }

        void OnGUI() {
            bool shouldRefresh = false;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, Array.Empty<GUILayoutOption>())) {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, noExpandWidth)) shouldRefresh = true;
                toolbarLabelStyle ??= GUI.skin.FindStyle("ToolbarLabel") ??
                    EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector).FindStyle("ToolbarLabel");
                GUILayout.Label($"Dependency Groups: {groups?.Length ?? 0}", toolbarLabelStyle, noExpandWidth);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Clean All", EditorStyles.toolbarButton, noExpandWidth))
                    shouldRefresh |= ConfirmAndCleanAll();
                skipConfirm = GUILayout.Toggle(skipConfirm, "Skip Confirmations", EditorStyles.toolbarButton, noExpandWidth);
            }
            using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPosition, Array.Empty<GUILayoutOption>())) {
                scrollPosition = scroll.scrollPosition;
                if (groups != null)
                    foreach (var root in groups)
                        if (DrawEntry(root, !root.IsEmpty, 0, ref shouldRefresh)) {
                            var e = root.GetEnumerator();
                            foreach (var current in e)
                                if (!DrawEntry(current, e.IsEnteringChildren, e.Depth * 16F, ref shouldRefresh))
                                    e.CancelYieldChildrenOnNext();
                        }
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.HelpBox("Hint: Double click on assets to select them,\nor drag to other properties to assign them there.", MessageType.Info);
            if (shouldRefresh) {
                AssetDatabase.SaveAssets();
                Refresh();
            }
        }

        bool DrawEntry(DependencyNode node, bool expandable, float indentOffset, ref bool shouldRefresh) {
            bool isHidden = (node.asset.hideFlags & (HideFlags.HideInHierarchy | HideFlags.HideInInspector)) != HideFlags.None;
            bool isExpanded = false;
            using (new EditorGUILayout.HorizontalScope(Array.Empty<GUILayoutOption>())) {
                var color = GUI.color;
                if (isHidden) GUI.color = color * 0.75F;
                GUIStyle style;
                if (expandable) {
                    expanded.TryGetValue(node.asset, out isExpanded);
                    style = EditorStyles.foldout;
                } else {
                    indentOffset += 13;
                    style = EditorStyles.label;
                }
                GUILayout.Space(indentOffset);
                var rect = GUILayoutUtility.GetRect(
                    0, position.width - indentOffset,
                    EditorGUIUtility.singleLineHeight,
                    EditorGUIUtility.singleLineHeight,
                    style
                );
                var controlID = GUIUtility.GetControlID(FocusType.Passive, rect);
                HandleClickDrag(rect, controlID, node);
                var content = TempContent(
                    ObjectNames.GetInspectorTitle(node.asset),
                    GetThumbnail(node.asset),
                    node.asset.name
                );
                using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
                    isExpanded = GUI.Toggle(rect, controlID, isExpanded, content, style);
                    if (changeCheck.changed) expanded[node.asset] = isExpanded;
                }
                GUI.color = color;
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(TempImageContent("TreeEditor.Trash", "Delete"), EditorStyles.iconButton, noExpandWidth))
                    shouldRefresh |= Event.current.shift ? ConfirmAndCleanDeep(node) : ConfirmAndCleanSingle(node);
            }
            return expandable && isExpanded;
        }

        void HandleClickDrag(Rect controlRect, int controlID, DependencyNode node) {
            var e = Event.current;
            if (!controlRect.Contains(e.mousePosition) || !node) return;
            switch (e.type) {
                case EventType.MouseDown:
                    switch (e.clickCount) {
                        case 2:
                            Selection.activeObject = node.asset;
                            e.Use();
                            break;
                    }
                    break;
                case EventType.MouseDrag:
                    DragAndDrop.PrepareStartDrag();
                    if (node.Equals(groups[0])) DragAndDrop.paths = new[] { mainAssetPath };
                    DragAndDrop.objectReferences = new[] { node.asset };
                    DragAndDrop.activeControlID = controlID;
                    DragAndDrop.StartDrag(node.asset.name);
                    e.Use();
                    break;
            }
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

        bool Cleanup(DependencyNode node, bool deep) {
            if (deep) {
                listed.Clear();
                if (groups != null) {
                    for (int i = node ? 1 : 0; i < groups.Length; i++) {
                        var currentDepd = groups[i];
                        if (currentDepd.Equals(node)) continue;
                        listed.Add(currentDepd);
                        var e = currentDepd.GetEnumerator();
                        foreach (var otherDepd in e)
                            if (!otherDepd.Equals(node) && !listed.Add(otherDepd))
                                e.CancelYieldChildrenOnNext();
                    }
                    if (node) {
                        foreach (var toDelete in node)
                            if (listed.Add(toDelete))
                                AssetDatabase.RemoveObjectFromAsset(toDelete.asset);
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

        sealed class AssetImportNotifier : AssetPostprocessor {
            void OnPostprocessAllAssets() {
                foreach (var window in openedWindows)
                    if (window) window.Refresh();
            }
        }
    }

    public sealed class DependencyNode : IEnumerable<DependencyNode>, IEquatable<DependencyNode> {
        public readonly UnityObject asset;
        DependencyNode[] dependencies = Array.Empty<DependencyNode>();

        public bool IsEmpty => dependencies.Length == 0;

        static bool IsVaildUnityObject(UnityObject asset) => asset;

        static DependencyNode FromMapping(Dictionary<UnityObject, DependencyNode> map, UnityObject asset) {
            if (!map.TryGetValue(asset, out var node)) map[asset] = node = new(asset);
            return node;
        }

        public static DependencyNode[] Scan(string assetPath) {
            var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (!mainAsset) return Array.Empty<DependencyNode>();
            var assetSet = new HashSet<UnityObject>((
                AssetDatabase.LoadAllAssetsAtPath(assetPath) ??
                Enumerable.Empty<UnityObject>()
            ).Where(IsVaildUnityObject)) { mainAsset };
            if (assetSet.Count == 0) return Array.Empty<DependencyNode>();
            var roots = new HashSet<UnityObject>(assetSet);
            var obj2RawDepds = new Dictionary<UnityObject, HashSet<UnityObject>>(assetSet.Count);
            foreach (var asset in assetSet) {
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

        public IDependencyNodeEnumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<DependencyNode> IEnumerable<DependencyNode>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Equals(DependencyNode other) => other != null && asset == other.asset;

        public override bool Equals(object obj) => Equals(obj as DependencyNode);

        public override int GetHashCode() => asset ? asset.GetHashCode() : 0;

        public override string ToString() => asset ? asset.name : "null";

        public static bool operator true(DependencyNode node) => node != null && node.asset;

        public static bool operator false(DependencyNode node) => !node;

        public static bool operator !(DependencyNode node) => node == null || !node.asset;

        sealed class Enumerator : Stack<EnumerateState>, IDependencyNodeEnumerator, ICollection {
            DependencyNode current;

            public DependencyNode Current => current;

            // Hides implementation in stack class
            int ICollection.Count => throw new NotSupportedException();

            public int Depth => IsEnteringChildren ? Count - 1 : Count;

            public bool IsEnteringChildren => TryPeek(out var entry) && entry.Equals(current);

            public Enumerator(DependencyNode root) {
                current = root;
                YieldChildrenOnNext();
            }

            public bool MoveNext() {
                while (TryPop(out var entry)) {
                    if (!entry.TryYield(out current)) continue;
                    Push(entry);
                    if (!current) continue;
                    foreach (var onStack in this as Stack<EnumerateState>)
                        if (onStack.Equals(current))
                            goto SkipCurrent;
                    YieldChildrenOnNext();
                    return true;
                    SkipCurrent:;
                }
                return false;
            }

            void YieldChildrenOnNext() {
                if (current.dependencies.Length > 0) Push(current);
            }

            public bool CancelYieldChildrenOnNext() {
                if (!IsEnteringChildren) return false;
                Pop();
                return true;
            }

            // Hides implementation in stack class
            IEnumerator IEnumerable.GetEnumerator() => this;

            // Hides implementation in stack class
            void ICollection.CopyTo(Array array, int index) => throw new NotSupportedException();

            void IEnumerator.Reset() => throw new NotSupportedException();

            void IDisposable.Dispose() => Clear();
        }

        struct EnumerateState : IEquatable<DependencyNode> {
            readonly DependencyNode node;
            int index;

            EnumerateState(DependencyNode node) {
                this.node = node;
                index = -1;
            }

            public bool TryYield(out DependencyNode current) {
                ref var dependencies = ref node.dependencies;
                if (++index < dependencies.Length) {
                    current = dependencies[index];
                    return true;
                }
                current = null;
                return false;
            }

            public readonly bool Equals(DependencyNode other) => node.Equals(other);

            public static implicit operator EnumerateState(DependencyNode node) => new(node);
        }
    }

    public interface IDependencyNodeEnumerator : IEnumerable<DependencyNode>, IEnumerator<DependencyNode> {
        object IEnumerator.Current => Current;

        int Depth { get; }

        bool IsEnteringChildren { get; }

        bool CancelYieldChildrenOnNext();

        IEnumerator<DependencyNode> IEnumerable<DependencyNode>.GetEnumerator() => this;

        IEnumerator IEnumerable.GetEnumerator() => this;
    }
}
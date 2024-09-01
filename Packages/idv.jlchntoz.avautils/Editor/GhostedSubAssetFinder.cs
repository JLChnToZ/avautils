using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.CommonUtils {
    public class GhostedSubAssetFinder : EditorWindow {
        const string MENU_PATH = "Assets/Find Ghosted Sub Assets";
        static readonly HashSet<GhostedSubAssetFinder> openedWindows = new();
        readonly HashSet<DependencyNode> listed = new();
        readonly Dictionary<int, int> instance2TreeId = new();
        [NonSerialized] DependencyNode[] groups;
        [SerializeField] string mainAssetPath;
        [SerializeField] bool skipConfirm;
        Label counterLabel;
        BaseTreeView treeView;
        bool isSelectionChanging;

        DependencyNode[] SelectedNodes {
            get {
                if (treeView == null || treeView.selectedIndex < 0)
                    return Array.Empty<DependencyNode>();
                var nodes = new List<DependencyNode>();
                foreach (var n in treeView.selectedItems)
                    if (n is DependencyNode depd && depd.asset) nodes.Add(depd);
                return nodes.ToArray();
            }
        }

        public static GhostedSubAssetFinder ShowWindow(UnityObject asset) =>
            ShowWindow(AssetDatabase.GetAssetPath(asset));

        public static GhostedSubAssetFinder ShowWindow(string path) {
            if (string.IsNullOrEmpty(path))
                return null;
            var window = CreateInstance<GhostedSubAssetFinder>();
            window.mainAssetPath = path;
            window.Show();
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

        static string PrettifyNames(DependencyNode[] nodes) {
            if (nodes == null || nodes.Length == 0) return "no assets";
            if (nodes.Length == 1) return nodes[0].asset.name;
            return $"{nodes.Length} sub assets";
        }

        static void OpenAssets(DependencyNode[] nodes) {
            foreach (var node in nodes)
                AssetDatabase.OpenAsset(node.asset);
        }

        static void PingAssets(DependencyNode[] nodes) {
            foreach (var node in nodes)
                EditorGUIUtility.PingObject(node.asset);
        }

        static void ShowProperties(DependencyNode[] nodes) {
            foreach (var node in nodes)
                EditorUtility.OpenPropertyEditor(node.asset);
        }

        void OnEnable() {
            if (!string.IsNullOrEmpty(mainAssetPath) && groups == null)
                Refresh();
            openedWindows.Add(this);
            minSize = new Vector2(400, 200);
            Selection.selectionChanged += SyncSelection;
        }

        void OnDisable() {
            openedWindows.Remove(this);
            Selection.selectionChanged -= SyncSelection;
        }

        void CreateGUI() {
            var root = rootVisualElement;
            var toolbar = new Toolbar();
            var labelContainer = new VisualElement();
            labelContainer.Add(counterLabel = new() { style = { paddingTop = 1, paddingLeft = 4 } });
            toolbar.Add(labelContainer);
            toolbar.Add(new ToolbarSpacer { flex = true });
            var refreshButton = new ToolbarButton(Refresh);
            refreshButton.Add(new Label("Refresh"));
            toolbar.Add(refreshButton);
            var cleanAllButton = new ToolbarButton(ConfirmAndCleanAll);
            cleanAllButton.Add(new Label("Clean All") {
                tooltip = "Delete all ghosted sub assets\n(Hidden and main asset did not depend on it)",
            });
            toolbar.Add(cleanAllButton);
            root.Add(toolbar);
            treeView = new TreeView(PrepareEntryView, BindEntryView) {
                fixedItemHeight = EditorGUIUtility.singleLineHeight,
                horizontalScrollingEnabled = true,
                selectionType = SelectionType.Multiple,
            };
            treeView.AddManipulator(new ContextualMenuManipulator(PrepareContextMenu));
            treeView.RegisterCallback<KeyDownEvent>(OnKeyDown);
            treeView.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            treeView.RegisterCallback<MouseDownEvent>(OnMouseDown);
            treeView.selectionChanged += SyncSelection;
            root.Add(treeView);
            Refresh();
        }

        static VisualElement PrepareEntryView() {
            var container = new VisualElement {
                style = {
                    flexDirection = FlexDirection.Row,
                },
            };
            container.Add(new Image {
                style = {
                    width = EditorGUIUtility.singleLineHeight,
                    height = EditorGUIUtility.singleLineHeight,
                },
            });
            container.Add(new Label {
                style = {
                    whiteSpace = WhiteSpace.NoWrap,
                },
            });
            return container;
        }

        void BindEntryView(VisualElement element, int index) {
            var asset = treeView.GetItemDataForIndex<DependencyNode>(index).asset;
            element.tooltip = asset.name;
            var style = element.style;
            style.unityFontStyleAndWeight = (asset == groups[0].asset) ? FontStyle.Bold : FontStyle.Normal;
            style.color = (asset.hideFlags & HideFlags.HideInHierarchy) != 0 ? Color.gray : StyleKeyword.Null;
            var label = element.Q<Label>();
            label.text = ObjectNames.GetInspectorTitle(asset);
            var image = element.Q<Image>();
            var thumbnail = AssetPreview.GetMiniThumbnail(asset);
            if (thumbnail.width == 0)
                thumbnail = AssetPreview.GetMiniTypeThumbnail(asset.GetType());
            image.image = thumbnail;
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
            titleContent.text = Path.GetFileNameWithoutExtension(mainAssetPath);
            titleContent.image = AssetDatabase.GetCachedIcon(mainAssetPath);
            titleContent = title;
            if (counterLabel != null) counterLabel.text = $"Dependency Groups: {groups.Length}";
            UpdateTree();
        }

        void UpdateTree() {
            if (treeView == null) return;
            int idCounter = 0;
            var depthLookup = new Dictionary<DependencyNode, (List<TreeViewItemData<DependencyNode>> parentChildren, int depth, int id)>();
            var treeParentStack = new Stack<List<TreeViewItemData<DependencyNode>>>();
            var rootItems = new List<TreeViewItemData<DependencyNode>>();
            instance2TreeId.Clear();
            foreach (var root in groups) {
                var rootChildren = new List<TreeViewItemData<DependencyNode>>();
                var rootData = new TreeViewItemData<DependencyNode>(idCounter++, root, rootChildren);
                instance2TreeId[root.asset.GetInstanceID()] = rootData.id;
                treeParentStack.Push(rootChildren);
                var e = root.GetEnumerator();
                int lastDepth = 0;
                foreach (var current in e) {
                    int currentDepth = e.Depth;
                    while (lastDepth > currentDepth) {
                        treeParentStack.Pop();
                        lastDepth--;
                    }
                    List<TreeViewItemData<DependencyNode>> children = null;
                    if (depthLookup.TryGetValue(current, out var lookup)) {
                        var (existsParantChildren, existDepth, id) = lookup;
                        if (existDepth > lastDepth)
                            for (int i = 0; i < existsParantChildren.Count; i++)
                                if (existsParantChildren[i].id == id) {
                                    children = existsParantChildren[i].children as List<TreeViewItemData<DependencyNode>>;
                                    existsParantChildren[i] = new(id, current);
                                    break;
                                }
                    }
                    var parentChildren = treeParentStack.Peek();
                    bool childrenResolved = children != null;
                    if (!childrenResolved) children = new List<TreeViewItemData<DependencyNode>>();
                    var nodeData = new TreeViewItemData<DependencyNode>(idCounter++, current, children);
                    parentChildren.Add(nodeData);
                    depthLookup[current] = (parentChildren, currentDepth, nodeData.id);
                    instance2TreeId[current.asset.GetInstanceID()] = nodeData.id;
                    if (childrenResolved) {
                        e.CancelYieldChildrenOnNext();
                        lastDepth = currentDepth;
                    } else if (e.IsEnteringChildren) {
                        treeParentStack.Push(children);
                        lastDepth = currentDepth + 1;
                    } else
                        lastDepth = currentDepth;
                }
                rootItems.Add(rootData);
                depthLookup.Clear();
                treeParentStack.Clear();
            }
            treeView.SetRootItems(rootItems);
            treeView.Rebuild();
            SyncSelection();
        }

        void PrepareContextMenu(ContextualMenuPopulateEvent e) {
            var nodes = SelectedNodes;
            if (nodes.Length == 0) return;
            var menu = e.menu;
            menu.AppendAction("Open...", OpenObjectMenu, CanOpenForEditMenu, nodes);
            menu.AppendAction("Show in Project Window", LocateObjectMenu, CanPingObjectMenu, nodes);
            menu.AppendSeparator();
            menu.AppendAction("Delete", CleanShallowMenu, DropdownMenuAction.AlwaysEnabled, nodes);
            menu.AppendAction("Delete All Descendants", CleanDeepMenu, DropdownMenuAction.AlwaysEnabled, nodes);
            menu.AppendSeparator();
            menu.AppendAction("Properties...", PropertiesMenu, DropdownMenuAction.AlwaysEnabled, nodes);
        }

        void OnKeyDown(KeyDownEvent e) {
            switch (e.keyCode) {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (e.altKey)
                        ShowProperties(SelectedNodes);
                    else if (Application.platform != RuntimePlatform.OSXEditor)
                        OpenAssets(SelectedNodes);
                    e.StopPropagation();
                    break;
                case KeyCode.Delete:
                    var nodes = SelectedNodes;
                    if (nodes.Length == 0) break;
                    skipConfirm = e.ctrlKey;
                    if (e.shiftKey)
                        ConfirmAndCleanDeep(nodes);
                    else
                        ConfirmAndCleanShallow(nodes);
                    skipConfirm = false;
                    e.StopPropagation();
                    break;
                case KeyCode.F5:
                    Refresh();
                    e.StopPropagation();
                    break;
                case KeyCode.O:
                    if (Application.platform == RuntimePlatform.OSXEditor && e.commandKey)
                        OpenAssets(SelectedNodes);
                    e.StopPropagation();
                    break;
            }
        }

        void OnMouseDown(MouseDownEvent e) {
            if (e.clickCount == 2) {
                OpenAssets(SelectedNodes);
                e.StopPropagation();
            }
        }

        void OnMouseMove(MouseMoveEvent e) {
            if ((e.pressedButtons & 0x1) == 0) return;
            var nodes = SelectedNodes;
            if (nodes.Length == 0) return;
            var objects = new UnityObject[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
                objects[i] = nodes[i].asset;
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = objects;
            if (nodes.Contains(groups[0])) DragAndDrop.paths = new[] { mainAssetPath };
            DragAndDrop.StartDrag(PrettifyNames(nodes));
        }

        static DropdownMenuAction.Status CanOpenForEditMenu(DropdownMenuAction action) {
            foreach (var node in action.userData as DependencyNode[])
                if (AssetDatabase.IsOpenForEdit(node.asset))
                    return DropdownMenuAction.Status.Normal;
            return DropdownMenuAction.Status.Disabled;
        }

        static DropdownMenuAction.Status CanPingObjectMenu(DropdownMenuAction action) {
            foreach (var node in action.userData as DependencyNode[])
                if ((node.asset.hideFlags & HideFlags.HideInHierarchy) == 0)
                    return DropdownMenuAction.Status.Normal;
            return DropdownMenuAction.Status.Disabled;
        }

        static void OpenObjectMenu(DropdownMenuAction action) => OpenAssets(action.userData as DependencyNode[]);

        static void LocateObjectMenu(DropdownMenuAction action) => PingAssets(action.userData as DependencyNode[]);

        static void PropertiesMenu(DropdownMenuAction action) => ShowProperties(action.userData as DependencyNode[]);

        void CleanShallowMenu(DropdownMenuAction action) => ConfirmAndCleanShallow(action.userData as DependencyNode[]);

        void CleanDeepMenu(DropdownMenuAction action) => ConfirmAndCleanDeep(action.userData as DependencyNode[]);

        void ConfirmAndCleanShallow(DependencyNode[] nodes) {
            if (skipConfirm || EditorUtility.DisplayDialog(
                "Delete Sub Assets",
                $"Are you sure you want to delete {PrettifyNames(nodes)}?",
                "Yes", "No"
            )) Cleanup(nodes, false);
        }

        void ConfirmAndCleanDeep(DependencyNode[] nodes) {
            if (skipConfirm || EditorUtility.DisplayDialog(
                "Delete All Descendants",
                $"Are you sure you want to delete {PrettifyNames(nodes)} and all its descendants?",
                "Yes", "No"
            )) Cleanup(nodes, true);
        }

        void ConfirmAndCleanAll() {
            if (skipConfirm || EditorUtility.DisplayDialog(
                "Delete All Ghosted Sub Assets",
                "Are you sure you want to delete all ghosted sub assets?",
                "Yes", "No"
            )) Cleanup(null, true);
        }

        void Cleanup(DependencyNode[] nodes, bool deep) {
            bool hasNodes = nodes != null && nodes.Length > 0;
            if (deep) {
                listed.Clear();
                if (groups != null) {
                    for (int i = hasNodes ? 1 : 0; i < groups.Length; i++) {
                        var currentDepd = groups[i];
                        if (hasNodes && Array.IndexOf(nodes, currentDepd) >= 0) continue;
                        listed.Add(currentDepd);
                        var e = currentDepd.GetEnumerator();
                        foreach (var otherDepd in e)
                            if ((!hasNodes || Array.IndexOf(nodes, otherDepd) < 0) && !listed.Add(otherDepd))
                                e.CancelYieldChildrenOnNext();
                    }
                    if (hasNodes) {
                        foreach (var node in nodes)
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
            if (hasNodes)
                foreach (var node in nodes)
                    if (node) AssetDatabase.RemoveObjectFromAsset(node.asset);
            AssetDatabase.SaveAssets();
            Refresh();
        }

        void SyncSelection() {
            if (isSelectionChanging || treeView == null) return;
            treeView.SetSelectionByIdWithoutNotify(GetSelectionTreeIds());
        }

        void SyncSelection(IEnumerable<object> selections) {
            var list = new List<UnityObject>();
            foreach (var id in selections)
                if (id is DependencyNode depd && depd.asset)
                    list.Add(depd.asset);
            try {
                isSelectionChanging = true;
                Selection.objects = list.ToArray();
            } finally {
                isSelectionChanging = false;
            }
        }

        IEnumerable<int> GetSelectionTreeIds() {
            var alreadySelected = new Dictionary<UnityObject, int>();
            foreach (var i in treeView.selectedIndices) {
                var node = treeView.GetItemDataForIndex<DependencyNode>(i);
                if (node != null && node.asset)
                    alreadySelected[node.asset] = treeView.GetIdForIndex(i);
            }
            foreach (var obj in Selection.objects)
                if (obj && (
                    alreadySelected.TryGetValue(obj, out var id) ||
                    instance2TreeId.TryGetValue(obj.GetInstanceID(), out id)
                )) yield return id;
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
        public readonly int instanceId;
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

        DependencyNode(UnityObject asset) {
            this.asset = asset;
            instanceId = asset ? asset.GetInstanceID() : 0;
        }

        public Enumerator GetEnumerator() => new(this);

        IEnumerator<DependencyNode> IEnumerable<DependencyNode>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Equals(DependencyNode other) => other != null && asset == other.asset;

        public override bool Equals(object obj) => Equals(obj as DependencyNode);

        public override int GetHashCode() => instanceId;

        public override string ToString() => asset ? asset.name : "null";

        public static bool operator true(DependencyNode node) => node != null && node.asset;

        public static bool operator false(DependencyNode node) => !node;

        public static bool operator !(DependencyNode node) => node == null || !node.asset;

        public readonly struct Enumerator : IEnumerable<DependencyNode>, IEnumerator<DependencyNode> {
            static readonly ConditionalWeakTable<Stack<State>, DependencyNode> states = new();
            readonly Stack<State> stack;

            public bool IsVaild => stack != null;

            public int Depth => IsEnteringChildren ? stack.Count - 1 : stack.Count;

            public bool IsEnteringChildren => IsVaild &&
                states.TryGetValue(stack, out var current) &&
                stack.TryPeek(out var entry) &&
                entry.Equals(current);

            public DependencyNode Current {
                get {
                    if (IsVaild && states.TryGetValue(stack, out var current))
                        return current;
                    throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                }
                private set {
                    if (value.dependencies.Length > 0) stack.Push(value);
                    states.AddOrUpdate(stack, value);
                }
            }

            object IEnumerator.Current => Current;

            public Enumerator(DependencyNode root) {
                if (!root) throw new ArgumentNullException(nameof(root));
                stack = new();
                Current = root;
            }

            public bool MoveNext() {
                if (!IsVaild) return false;
                while (stack.TryPop(out var entry)) {
                    if (!entry.TryYield(out var current)) continue;
                    stack.Push(entry);
                    if (!current) continue;
                    foreach (var onStack in stack)
                        if (onStack.Equals(current))
                            goto SkipCurrent;
                    Current = current;
                    return true;
                    SkipCurrent:;
                }
                states.Remove(stack);
                return false;
            }

            public bool CancelYieldChildrenOnNext() {
                if (!IsEnteringChildren) return false;
                stack.Pop();
                return true;
            }

            void IEnumerator.Reset() => throw new NotSupportedException();

            public void Dispose() => states.Remove(stack);

            public Enumerator GetEnumerator() => this;

            IEnumerator<DependencyNode> IEnumerable<DependencyNode>.GetEnumerator() => this;

            IEnumerator IEnumerable.GetEnumerator() => this;

            struct State : IEquatable<DependencyNode> {
                readonly DependencyNode node;
                int index;

                State(DependencyNode node) {
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

                public static implicit operator State(DependencyNode node) => new(node);
            }
        }
    }
}
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Buffers;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.CommonUtils {
    public class GhostedSubAssetFinder : EditorWindow {
        const string MENU_PATH = "Assets/Find Ghosted Sub Assets";
        static readonly HashSet<GhostedSubAssetFinder> openedWindows = new();
        readonly HashSet<DependencyNode> temp = new();
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
            var title = titleContent;
            titleContent.text = "Ghosted Sub Asset Finder";
            titleContent = title;
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
            treeView.Q("unity-content-and-vertical-scroll-container")?.RegisterCallback<MouseDownEvent>(OnEmptySpaceClick);
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
            temp.Clear();
            foreach (var root in groups) {
                temp.Add(root);
                using var e = root.GetDeepEnumerator();
                foreach (var node in e)
                    if (!temp.Add(node))
                        e.SkipCurrentChildren();
            }
            if (counterLabel != null) counterLabel.text = $"{groups.Length} Group(s), {temp.Count} Asset(s)";
            temp.Clear();
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
                using var e = root.GetDeepEnumerator();
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
                        e.SkipCurrentChildren();
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

        void OnEmptySpaceClick(MouseDownEvent e) {
            if (e.target == e.currentTarget)
                treeView.ClearSelection();
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
                temp.Clear();
                if (groups != null) {
                    for (int i = hasNodes ? 1 : 0; i < groups.Length; i++) {
                        var currentDepd = groups[i];
                        if (hasNodes && Array.IndexOf(nodes, currentDepd) >= 0) continue;
                        temp.Add(currentDepd);
                        using var e = currentDepd.GetDeepEnumerator();
                        foreach (var otherDepd in e)
                            if ((!hasNodes || Array.IndexOf(nodes, otherDepd) < 0) && !temp.Add(otherDepd))
                                e.SkipCurrentChildren();
                    }
                    if (hasNodes) {
                        foreach (var node in nodes)
                            foreach (var toDelete in node.GetDeepEnumerator())
                                if (temp.Add(toDelete))
                                    AssetDatabase.RemoveObjectFromAsset(toDelete.asset);
                    } else {
                        temp.Remove(groups[0]);
                        temp.ExceptWith(groups[0].GetDeepEnumerator());
                        foreach (var toDelete in temp)
                            AssetDatabase.RemoveObjectFromAsset(toDelete.asset);
                    }
                    temp.Clear();
                }
            }
            if (hasNodes)
                foreach (var node in nodes)
                    if (DependencyNode.IsValid(node))
                        AssetDatabase.RemoveObjectFromAsset(node.asset);
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

        public bool HasDependencies => dependencies.Length > 0;

        public static bool IsValid(DependencyNode node) => node != null && node.asset != null;

        public static DependencyNode[] Scan(string assetPath) => default(ScanContext).Scan(assetPath);

        DependencyNode(UnityObject asset) {
            this.asset = asset;
            instanceId = asset ? asset.GetInstanceID() : 0;
        }

        public DeepEnumerator GetDeepEnumerator() => new(this);

        public Enumerator GetEnumerator() => new(this);

        IEnumerator<DependencyNode> IEnumerable<DependencyNode>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Equals(DependencyNode other) => other != null && asset == other.asset;

        public override bool Equals(object obj) => Equals(obj as DependencyNode);

        public override int GetHashCode() => instanceId;

        public override string ToString() => asset ? asset.name : "null";

        public struct Enumerator : IEnumerator<DependencyNode> {
            readonly DependencyNode node;
            int index;

            public readonly DependencyNode Current => node.dependencies[index];

            readonly object IEnumerator.Current => Current;

            public Enumerator(DependencyNode node) {
                this.node = node;
                index = -1;
            }

            public bool MoveNext() {
                ref var dependencies = ref node.dependencies;
                while (++index < dependencies.Length)
                    if (IsValid(dependencies[index]))
                        return true;
                return false;
            }

            public readonly bool IsEnumeratorOf(DependencyNode other) => node.Equals(other);

            public void Reset() => index = -1;

            public readonly void Dispose() { }
        }

        public sealed class DeepEnumerator : IEnumerable<DependencyNode>, IEnumerator<DependencyNode> {
            Enumerator[] stack;
            DependencyNode current;
            int count = 0;

            public int Depth => count + (IsEnteringChildren ? -1 : 0);

            public bool IsEnteringChildren => IsValid(current) && count > 0 && stack[count - 1].IsEnumeratorOf(current);

            public DependencyNode Current => current;

            object IEnumerator.Current => current;

            public DeepEnumerator(DependencyNode root) {
                if (!IsValid(root)) throw new ArgumentNullException(nameof(root));
                Push(root);
            }

            public bool MoveNext() {
                while (count > 0) {
                    ref var entry = ref stack[--count];
                    if (!entry.MoveNext()) continue;
                    var current = entry.Current;
                    count++;
                    for (int i = count - 1; i >= 0; i--)
                        if (stack[i].IsEnumeratorOf(current))
                            goto SkipCurrent;
                    Push(current);
                    return true;
                    SkipCurrent: ;
                }
                return false;
            }

            void Push(DependencyNode value) {
                current = value;
                if (!current.HasDependencies) return;
                var pool = ArrayPool<Enumerator>.Shared;
                if (stack == null)
                    stack = pool.Rent(8);
                else if (count >= stack.Length) {
                    var newStack = pool.Rent(stack.Length << 1);
                    stack.CopyTo(newStack, 0);
                    pool.Return(stack);
                    stack = newStack;
                }
                stack[count++] = new(current);
            }

            public bool SkipCurrentChildren() {
                bool isEnteringChildren = IsEnteringChildren;
                if (isEnteringChildren) count--;
                return isEnteringChildren;
            }

            void IEnumerator.Reset() => throw new NotSupportedException();

            public void Dispose() {
                current = null;
                if (stack == null) return;
                ArrayPool<Enumerator>.Shared.Return(stack);
                stack = null;
            }

            IEnumerator<DependencyNode> IEnumerable<DependencyNode>.GetEnumerator() => this;

            IEnumerator IEnumerable.GetEnumerator() => this;
        }

        struct ScanContext {
            string mainAssetPath;
            UnityObject mainAsset;
            HashSet<UnityObject> assetSet;
            HashSet<UnityObject> roots;
            Dictionary<UnityObject, HashSet<UnityObject>> obj2RawDepds;
            Dictionary<UnityObject, DependencyNode> obj2Nodes;
            List<DependencyNode> result;

            static bool IsVaildUnityObject(UnityObject asset) => asset != null;

            public DependencyNode[] Scan(string assetPath) {
                if (!LoadMainAsset(assetPath))
                    return Array.Empty<DependencyNode>();
                if (!CollectSubAssets())
                    return new[] { new DependencyNode(mainAsset) };
                MapObjectToRawDependencies();
                MapObjectToNodes();
                return GatherResult();
            }

            bool LoadMainAsset(string assetPath) {
                mainAssetPath = assetPath;
                mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                return mainAsset;
            }

            bool CollectSubAssets() {
                if (mainAsset is SceneAsset) return false; // Scene asset is not supported
                assetSet = new(
                    AssetDatabase.LoadAllAssetsAtPath(mainAssetPath)?.Where(IsVaildUnityObject) ??
                    Enumerable.Empty<UnityObject>()
                ) { mainAsset };
                return assetSet.Count > 1;
            }

            void MapObjectToRawDependencies() {
                roots = new(assetSet);
                obj2RawDepds = new(assetSet.Count);
                foreach (var asset in assetSet) {
                    if (!asset) continue;
                    using var so = new SerializedObject(asset);
                    for (var sp = so.GetIterator(); sp.Next(true); )
                        if (sp.propertyType == SerializedPropertyType.ObjectReference)
                            MapRawDepds(asset, sp.objectReferenceValue);
                }
            }

            readonly bool MapRawDepds(UnityObject asset, UnityObject depd) {
                if (!depd || depd == asset || !assetSet.Contains(depd))
                    return false;
                if (!obj2RawDepds.TryGetValue(asset, out var rawDepds))
                    obj2RawDepds[asset] = rawDepds = new();
                rawDepds.Add(depd);
                roots.Remove(depd);
                return true;
            }

            readonly DependencyNode FromMapping(UnityObject asset) {
                if (!obj2Nodes.TryGetValue(asset, out var node))
                    obj2Nodes[asset] = node = new(asset);
                return node;
            }

            void MapObjectToNodes() {
                obj2Nodes = new(obj2RawDepds.Count);
                foreach (var asset in assetSet) {
                    if (!asset) continue;
                    var node = FromMapping(asset);
                    if (!obj2RawDepds.TryGetValue(asset, out var rawDepds))
                        continue;
                    var dependencies = new DependencyNode[rawDepds.Count];
                    node.dependencies = dependencies;
                    int i = 0;
                    foreach (var other in rawDepds)
                        dependencies[i++] = FromMapping(other);
                }
            }

            DependencyNode[] GatherResult() {
                result = new(roots.Count);
                if (mainAsset) {
                    roots.Remove(mainAsset);
                    result.Add(obj2Nodes[mainAsset]);
                }
                foreach (var root in roots)
                    if (root && obj2Nodes.TryGetValue(root, out var node))
                        result.Add(node);
                return result.ToArray();
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace TerraKit.Editor
{
    internal sealed class TerraKitGraphView : GraphView
    {
        private const string ClipboardPrefix = "TERRAKIT_NODES_V1:";
        private static readonly Vector2 DuplicateOffset = new Vector2(30, 30);
        private static readonly Vector2 NodeSearchSize = new Vector2(280, 180);
        private readonly EditorWindow _ownerWindow;

        [Serializable]
        private sealed class NodeClipboardPayload
        {
            public List<TerraKitNodeData> nodes = new List<TerraKitNodeData>();
            public List<TerraKitEdgeData> edges = new List<TerraKitEdgeData>();
        }

        private readonly Dictionary<string, TerraKitNodeView> _nodeViews = new Dictionary<string, TerraKitNodeView>();
        private TerraKitGraphAsset _graphAsset;
        private TerraKitNodeSearchProvider _nodeSearchProvider;

        public event Action<TerraKitNodeView> NodeSelected;
        public event Action GraphChanged;

        public TerraKitGraphView(EditorWindow ownerWindow)
        {
            _ownerWindow = ownerWindow;
            AddToClassList("terrakit-graph-view");
            focusable = true;

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            graphViewChanged = OnGraphViewChanged;
            serializeGraphElements = SerializeTerraKitElements;
            canPasteSerializedData = CanPasteTerraKitData;
            unserializeAndPaste = PasteTerraKitData;
            RegisterCallback<MouseDownEvent>(evt => Focus());

            _nodeSearchProvider = ScriptableObject.CreateInstance<TerraKitNodeSearchProvider>();
            _nodeSearchProvider.hideFlags = HideFlags.HideAndDontSave;
        }

        public void Load(TerraKitGraphAsset graphAsset, bool frameAll = true)
        {
            // Temporarily detach the asset so clearing old visual elements does not mutate saved graph data.
            _graphAsset = null;
            ClearSelection();
            DeleteElements(graphElements.ToList());
            _nodeViews.Clear();

            _graphAsset = graphAsset;
            if (_graphAsset == null)
            {
                return;
            }

            foreach (var nodeData in _graphAsset.nodes)
            {
                AddNodeView(nodeData);
            }

            foreach (var edgeData in _graphAsset.edges.ToList())
            {
                AddEdgeView(edgeData);
            }

            // Newly-created node views do not have final geometry until UI Toolkit completes a layout pass.
            // Wait until every node reports a real size before calculating the overview bounds.
            if (frameAll)
            {
                FrameAllWhenReady(graphAsset, 10);
            }
        }

        private void FrameAllWhenReady(TerraKitGraphAsset loadedAsset, int remainingAttempts)
        {
            schedule.Execute(() =>
            {
                if (_graphAsset != loadedAsset)
                {
                    return;
                }

                bool geometryIsReady = _nodeViews.Count == 0 ||
                    _nodeViews.Values.All(node => node.layout.width > 0 && node.layout.height > 0);

                if (geometryIsReady || remainingAttempts <= 1)
                {
                    FrameAll();
                    return;
                }

                FrameAllWhenReady(loadedAsset, remainingAttempts - 1);
            }).ExecuteLater(16);
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var startBinding = startPort.userData as TerraKitEditorPortBinding;
            if (startBinding == null)
            {
                return new List<Port>();
            }

            return ports.ToList().Where(candidate =>
            {
                if (candidate == startPort)
                {
                    return false;
                }

                var candidateBinding = candidate.userData as TerraKitEditorPortBinding;
                if (candidateBinding == null)
                {
                    return false;
                }

                if (candidateBinding.NodeId == startBinding.NodeId)
                {
                    return false;
                }

                if (candidateBinding.Direction == startBinding.Direction)
                {
                    return false;
                }

                var output = startBinding.Direction == Direction.Output ? startBinding : candidateBinding;
                var input = startBinding.Direction == Direction.Input ? startBinding : candidateBinding;
                return TerraKitNodeRegistry.ArePortTypesCompatible(output.Type, input.Type);
            }).ToList();
        }

        public override void AddToSelection(ISelectable selectable)
        {
            base.AddToSelection(selectable);

            var node = selectable as TerraKitNodeView;
            if (node != null)
            {
                NodeSelected?.Invoke(node);
            }
        }

        public override void ClearSelection()
        {
            base.ClearSelection();
            NodeSelected?.Invoke(null);
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);

            if (_graphAsset == null)
            {
                evt.menu.AppendAction("Create or assign a TerraKit graph first", null, DropdownMenuAction.Status.Disabled);
                return;
            }

            // Capture the original right-click coordinates now. The action callback
            // runs after the native context menu is clicked, when eventInfo may point
            // at the menu item instead of the place where the user right-clicked.
            var panelMousePosition = evt.mousePosition;
            var graphPosition = contentViewContainer.WorldToLocal(panelMousePosition);
            // UI Toolkit reports panel coordinates; IMGUI's current origin is not reliable here.
            var windowMousePosition = _ownerWindow.rootVisualElement.WorldToLocal(panelMousePosition);
            var screenPosition = _ownerWindow.position.position + windowMousePosition;

            evt.menu.AppendAction(
                "Create Node...",
                action =>
                {
                    if (_nodeSearchProvider == null)
                    {
                        _nodeSearchProvider = ScriptableObject.CreateInstance<TerraKitNodeSearchProvider>();
                        _nodeSearchProvider.hideFlags = HideFlags.HideAndDontSave;
                    }

                    _nodeSearchProvider.Initialize(this, graphPosition);
                    bool opened = SearchWindow.Open(
                        new SearchWindowContext(
                            screenPosition,
                            requestedWidth: NodeSearchSize.x,
                            requestedHeight: NodeSearchSize.y),
                        _nodeSearchProvider);
                    if (!opened) return;

                    var searchWindow = Resources.FindObjectsOfTypeAll<SearchWindow>().FirstOrDefault();
                    if (searchWindow == null) return;

                    // Override SearchWindow's minimum height and centered anchor after initialization.
                    searchWindow.minSize = NodeSearchSize;
                    searchWindow.maxSize = NodeSearchSize;
                    searchWindow.ShowAsDropDown(new Rect(screenPosition, Vector2.one), NodeSearchSize);
                });
        }

        public void CreateNode(ITerraKitNodeDefinition definition, Vector2 graphPosition)
        {
            if (_graphAsset == null || definition == null)
            {
                return;
            }

            Undo.RecordObject(_graphAsset, "Create TerraKit Node");
            var nodeData = TerraKitGraphEditorUtil.CreateNodeData(definition, graphPosition);
            _graphAsset.nodes.Add(nodeData);
            AddNodeView(nodeData);
            TerraKitGraphEditorUtil.MarkDirty(_graphAsset);
            GraphChanged?.Invoke();
        }

        private string SerializeTerraKitElements(IEnumerable<GraphElement> elements)
        {
            var payload = new NodeClipboardPayload
            {
                nodes = elements
                    .OfType<TerraKitNodeView>()
                    .Select(node => node.Data)
                    .ToList()
            };

            var selectedIds = new HashSet<string>(payload.nodes.Select(node => node.id));
            if (_graphAsset != null)
            {
                payload.edges = _graphAsset.edges
                    .Where(edge => selectedIds.Contains(edge.outputNodeId) &&
                                   selectedIds.Contains(edge.inputNodeId))
                    .ToList();
            }

            return payload.nodes.Count == 0
                ? string.Empty
                : ClipboardPrefix + JsonUtility.ToJson(payload);
        }

        private bool CanPasteTerraKitData(string data)
        {
            return _graphAsset != null &&
                   !string.IsNullOrEmpty(data) &&
                   data.StartsWith(ClipboardPrefix, StringComparison.Ordinal);
        }

        private void PasteTerraKitData(string operationName, string data)
        {
            if (!CanPasteTerraKitData(data))
            {
                return;
            }

            NodeClipboardPayload payload;
            try
            {
                payload = JsonUtility.FromJson<NodeClipboardPayload>(
                    data.Substring(ClipboardPrefix.Length));
            }
            catch (ArgumentException)
            {
                return;
            }

            if (payload == null || payload.nodes == null || payload.nodes.Count == 0)
            {
                return;
            }

            Undo.RecordObject(_graphAsset, operationName ?? "Duplicate TerraKit Node");
            var createdViews = new List<TerraKitNodeView>();
            var copiesBySourceId = new Dictionary<string, TerraKitNodeView>();

            foreach (var source in payload.nodes)
            {
                if (source == null || string.IsNullOrEmpty(source.id) ||
                    copiesBySourceId.ContainsKey(source.id))
                {
                    continue;
                }

                ITerraKitNodeDefinition definition;
                if (!TerraKitNodeRegistry.TryGet(source.typeId, out definition))
                {
                    continue;
                }

                var duplicate = TerraKitGraphEditorUtil.CreateNodeData(
                    definition,
                    source.position + DuplicateOffset);
                duplicate.schemaVersion = source.schemaVersion;
                duplicate.displayName = source.displayName;
                duplicate.size = source.size;
                duplicate.parameters = source.parameters == null
                    ? new List<TerraKitParameterData>()
                    : source.parameters
                        .Where(parameter => parameter != null)
                        .Select(parameter => new TerraKitParameterData
                        {
                            key = parameter.key,
                            displayName = parameter.displayName,
                            type = parameter.type,
                            value = parameter.value
                        }).ToList();
                TerraKitGraphEditorUtil.EnsureParameterDefaults(duplicate, definition);

                _graphAsset.nodes.Add(duplicate);
                var duplicateView = AddNodeView(duplicate);
                if (duplicateView != null)
                {
                    createdViews.Add(duplicateView);
                    copiesBySourceId.Add(source.id, duplicateView);
                }
            }

            if (createdViews.Count == 0)
            {
                return;
            }

            if (payload.edges != null)
            {
                foreach (var sourceEdge in payload.edges)
                {
                    TerraKitNodeView outputNode;
                    TerraKitNodeView inputNode;
                    if (sourceEdge == null ||
                        string.IsNullOrEmpty(sourceEdge.outputNodeId) ||
                        string.IsNullOrEmpty(sourceEdge.inputNodeId) ||
                        !copiesBySourceId.TryGetValue(sourceEdge.outputNodeId, out outputNode) ||
                        !copiesBySourceId.TryGetValue(sourceEdge.inputNodeId, out inputNode))
                    {
                        continue;
                    }

                    var output = outputNode.GetOutputPort(sourceEdge.outputPortId);
                    var input = inputNode.GetInputPort(sourceEdge.inputPortId);
                    if (output == null || input == null)
                    {
                        continue;
                    }

                    var edge = TerraKitGraphEditorUtil.CreateEdgeData(
                        (TerraKitEditorPortBinding)output.userData,
                        (TerraKitEditorPortBinding)input.userData);
                    _graphAsset.edges.Add(edge);
                    AddEdgeView(edge);
                }
            }

            ClearSelection();
            foreach (var createdView in createdViews)
            {
                AddToSelection(createdView);
            }

            TerraKitGraphEditorUtil.MarkDirty(_graphAsset);
            GraphChanged?.Invoke();
        }

        public void SyncNodeLayoutsToAsset()
        {
            if (_graphAsset == null)
            {
                return;
            }

            foreach (var nodeView in _nodeViews.Values)
            {
                nodeView.CaptureCurrentLayout();
            }

            EditorUtility.SetDirty(_graphAsset);
        }

        public bool FocusSelection()
        {
            if (selection == null || selection.Count == 0)
            {
                return false;
            }

            FrameSelection();
            return true;
        }

        public void SetValidationErrors(IEnumerable<TerraKitGraphCompileIssue> issues)
        {
            foreach (var nodeView in _nodeViews.Values)
            {
                nodeView.SetValidationErrorCount(0);
            }

            if (issues == null)
            {
                return;
            }

            foreach (var group in issues
                .Where(issue => issue != null && !string.IsNullOrEmpty(issue.NodeId))
                .GroupBy(issue => issue.NodeId))
            {
                TerraKitNodeView nodeView;
                if (_nodeViews.TryGetValue(group.Key, out nodeView))
                {
                    nodeView.SetValidationErrorCount(group.Count());
                }
            }
        }

        public bool GoToNode(string nodeId)
        {
            TerraKitNodeView nodeView;
            if (string.IsNullOrEmpty(nodeId) || !_nodeViews.TryGetValue(nodeId, out nodeView))
            {
                return false;
            }

            ClearSelection();
            AddToSelection(nodeView);
            FrameSelection();
            Focus();
            return true;
        }

        public void ResetZoomTo100Percent()
        {
#pragma warning disable CS0618 // GraphView still exposes its view transform through the legacy ITransform API.
            float currentScale = viewTransform.scale.x;
            if (currentScale <= 0)
            {
                currentScale = 1;
            }

            Vector2 viewportCenter = contentRect.center;
            Vector2 currentTranslation = new Vector2(viewTransform.position.x, viewTransform.position.y);
            Vector2 graphPointAtCenter = (viewportCenter - currentTranslation) / currentScale;
            Vector2 newTranslation = viewportCenter - graphPointAtCenter;

            UpdateViewTransform(
                new Vector3(newTranslation.x, newTranslation.y, 0),
                Vector3.one);
#pragma warning restore CS0618
        }

        private TerraKitNodeView AddNodeView(TerraKitNodeData nodeData)
        {
            ITerraKitNodeDefinition definition;
            if (!TerraKitNodeRegistry.TryGet(nodeData.typeId, out definition))
            {
                Debug.LogWarning("Skipping unknown TerraKit node type: " + nodeData.typeId);
                return null;
            }

            var nodeView = new TerraKitNodeView(nodeData, definition, _graphAsset);
            nodeView.Resized += OnNodeResized;
            _nodeViews[nodeData.id] = nodeView;
            AddElement(nodeView);
            return nodeView;
        }

        private void OnNodeResized()
        {
            if (_graphAsset != null)
            {
                TerraKitGraphEditorUtil.MarkDirty(_graphAsset);
                GraphChanged?.Invoke();
            }
        }

        private void AddEdgeView(TerraKitEdgeData edgeData)
        {
            TerraKitNodeView outputNode;
            TerraKitNodeView inputNode;

            if (!_nodeViews.TryGetValue(edgeData.outputNodeId, out outputNode) ||
                !_nodeViews.TryGetValue(edgeData.inputNodeId, out inputNode))
            {
                return;
            }

            var outputPort = outputNode.GetOutputPort(edgeData.outputPortId);
            var inputPort = inputNode.GetInputPort(edgeData.inputPortId);

            if (outputPort == null || inputPort == null)
            {
                return;
            }

            var edge = outputPort.ConnectTo(inputPort);
            edge.userData = edgeData.id;
            AddElement(edge);
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (_graphAsset == null)
            {
                return change;
            }

            bool dirty = false;

            if (change.elementsToRemove != null)
            {
                foreach (var element in change.elementsToRemove)
                {
                    var node = element as TerraKitNodeView;
                    if (node != null)
                    {
                        Undo.RecordObject(_graphAsset, "Delete TerraKit Node");
                        _graphAsset.nodes.RemoveAll(n => n.id == node.Data.id);
                        _graphAsset.edges.RemoveAll(e => e.inputNodeId == node.Data.id || e.outputNodeId == node.Data.id);
                        _nodeViews.Remove(node.Data.id);
                        dirty = true;
                        continue;
                    }

                    var edge = element as Edge;
                    if (edge != null)
                    {
                        var output = edge.output?.userData as TerraKitEditorPortBinding;
                        var input = edge.input?.userData as TerraKitEditorPortBinding;
                        if (output != null && input != null)
                        {
                            Undo.RecordObject(_graphAsset, "Delete TerraKit Edge");
                            _graphAsset.edges.RemoveAll(e =>
                                e.outputNodeId == output.NodeId &&
                                e.outputPortId == output.PortId &&
                                e.inputNodeId == input.NodeId &&
                                e.inputPortId == input.PortId);
                            dirty = true;
                        }
                    }
                }
            }

            if (change.edgesToCreate != null)
            {
                foreach (var edge in change.edgesToCreate)
                {
                    var output = edge.output?.userData as TerraKitEditorPortBinding;
                    var input = edge.input?.userData as TerraKitEditorPortBinding;
                    if (output == null || input == null)
                    {
                        continue;
                    }

                    Undo.RecordObject(_graphAsset, "Create TerraKit Edge");

                    // Inputs are single-connection by default. Replace existing incoming edge for the same input port.
                    _graphAsset.edges.RemoveAll(e => e.inputNodeId == input.NodeId && e.inputPortId == input.PortId);

                    bool alreadyExists = _graphAsset.edges.Any(e =>
                        e.outputNodeId == output.NodeId &&
                        e.outputPortId == output.PortId &&
                        e.inputNodeId == input.NodeId &&
                        e.inputPortId == input.PortId);

                    if (!alreadyExists)
                    {
                        var edgeData = TerraKitGraphEditorUtil.CreateEdgeData(output, input);
                        _graphAsset.edges.Add(edgeData);
                        edge.userData = edgeData.id;
                        dirty = true;
                    }
                }
            }

            if (change.movedElements != null && change.movedElements.Count > 0)
            {
                foreach (var node in change.movedElements.OfType<TerraKitNodeView>())
                {
                    node.CaptureCurrentLayout();
                }
                dirty = true;
            }

            if (dirty)
            {
                TerraKitGraphEditorUtil.MarkDirty(_graphAsset);
                GraphChanged?.Invoke();
            }

            return change;
        }
    }
}
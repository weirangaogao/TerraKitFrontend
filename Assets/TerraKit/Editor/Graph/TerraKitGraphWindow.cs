using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TerraKit.Editor
{
    public sealed class TerraKitGraphWindow : EditorWindow
    {
        private TerraKitGraphAsset _graphAsset;
        private TerraKitGraphView _graphView;
        private VisualElement _inspector;
        private TwoPaneSplitView _bodySplitView;
        private VisualElement _statusPanel;
        private Label _statusIcon;
        private Label _statusType;
        private Label _statusTime;
        private Label _statusSummary;
        private VisualElement _statusDetails;
        private ObjectField _graphField;
        private Label _saveStateLabel;

        private Button _saveButton;
        private Button _validateButton;
        private Button _frameAllButton;
        private Button _focusButton;
        private Button _resetZoomButton;
        private Button _generatorButton;
        private TerraKitNodeView _selectedNode;

        private enum StatusType
        {
            Info,
            Success,
            Warning,
            Error
        }

        [MenuItem("Tools/TerraKit/Graph Editor")]
        public static void Open()
        {
            var window = GetWindow<TerraKitGraphWindow>();
            window.titleContent = new GUIContent("TerraKit Graph");
            window.minSize = new Vector2(1100, 650);
        }

        internal static void OpenForGraph(TerraKitGraphAsset graph, bool validate = false,
            string error = null, TerraKitGraphCompileResult result = null)
        {
            Open();
            if (graph == null) return;
            var window = GetWindow<TerraKitGraphWindow>();
            window.rootVisualElement.schedule.Execute(() =>
            {
                window._graphAsset = graph;
                window._graphField.SetValueWithoutNotify(graph);
                window.LoadGraphIntoView();

                if (result != null)
                    window.DisplayValidationResult(result);
                else if (validate)
                    window.ValidateGraph();
                else if (!string.IsNullOrEmpty(error))
                    window.SetStatus(StatusType.Error, "Operation failed.", error);
            });
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += HandleUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= HandleUndoRedo;
        }

        private void HandleUndoRedo()
        {
            if (_graphAsset == null || _graphView == null || _statusDetails == null)
            {
                return;
            }

            LoadGraphIntoView(false);
            TerraKitGraphEditorUtil.MarkDirty(_graphAsset);
            SetStatus(
                StatusType.Info,
                "Undo / Redo applied and saved.",
                "Click Validate to check the updated graph.");
        }

        private void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.AddToClassList("terrakit-root");

            LoadStylesheet();
            BuildToolbar();
            BuildMainLayout();
            BuildConsole();

            ShowEmptyInspector();
        }

        private void BuildToolbar()
        {
            var toolbar = new Toolbar();
            toolbar.AddToClassList("terrakit-toolbar");

            var title = new Label("TerraKit Pipeline Graph");
            title.AddToClassList("terrakit-toolbar-title");
            toolbar.Add(title);

            _graphField = new ObjectField
            {
                objectType = typeof(TerraKitGraphAsset),
                allowSceneObjects = false,
                value = _graphAsset
            };
            _graphField.AddToClassList("terrakit-graph-field");
            _graphField.RegisterValueChangedCallback(evt =>
            {
                _graphAsset = evt.newValue as TerraKitGraphAsset;
                LoadGraphIntoView();
            });
            toolbar.Add(_graphField);

            _saveStateLabel = new Label();
            _saveStateLabel.AddToClassList("terrakit-save-state");
            toolbar.Add(_saveStateLabel);

            toolbar.Add(new Button(CreateGraphAsset) { text = "New Graph" });

            _saveButton = new Button(SaveGraph) { text = "Save" };
            _validateButton = new Button(ValidateGraph)
            {
                text = "Validate",
                tooltip = "Check parameters and connections without generating terrain. Show errors and execution order."
            };

            toolbar.Add(_saveButton);
            toolbar.Add(_validateButton);

            var backendDemoButton = new Button(CreateBackendDemoGraph)
            {
                text = "Demo Graph",
                tooltip = "Create a new backend-executable demo graph without modifying the current graph."
            };
            toolbar.Add(backendDemoButton);

            _frameAllButton = new Button(FrameAllNodes) { text = "Frame All" };
            toolbar.Add(_frameAllButton);

            _focusButton = new Button(FocusSelectedNode)
            {
                text = "Focus",
                tooltip = "Fit the selected node in the canvas. Shortcut: F."
            };
            toolbar.Add(_focusButton);

            _resetZoomButton = new Button(ResetZoom)
            {
                text = "100%",
                tooltip = "Reset zoom to 100% while keeping the current centre."
            };
            toolbar.Add(_resetZoomButton);

            _generatorButton = new Button(() => TerraKitBackendGeneratorWindow.OpenForGraph(_graphAsset))
            {
                text = "Generator"
            };
            toolbar.Add(_generatorButton);

            toolbar.Add(new Button(ShowHelp)
            {
                text = "Help",
                tooltip = "Show quick start instructions in the Info panel."
            });

            rootVisualElement.Add(toolbar);
        }

        private void BuildMainLayout()
        {
            _bodySplitView = new TwoPaneSplitView(
                1,
                210,
                TwoPaneSplitViewOrientation.Vertical)
            {
                viewDataKey = "terrakit-body-split"
            };
            _bodySplitView.AddToClassList("terrakit-body-split");

            var main = new TwoPaneSplitView(
                1,
                360,
                TwoPaneSplitViewOrientation.Horizontal)
            {
                viewDataKey = "terrakit-inspector-split"
            };
            main.AddToClassList("terrakit-main");

            _graphView = new TerraKitGraphView(this);
            _graphView.StretchToParentSize();
            _graphView.NodeSelected += HandleNodeSelected;
            _graphView.GraphChanged += HandleGraphChanged;

            var graphContainer = new VisualElement();
            graphContainer.AddToClassList("terrakit-graph-container");
            graphContainer.Add(_graphView);
            graphContainer.Add(BuildNavigationHint());

            _inspector = new ScrollView();
            _inspector.AddToClassList("terrakit-inspector");

            main.Add(graphContainer);
            main.Add(_inspector);
            _bodySplitView.Add(main);
            rootVisualElement.Add(_bodySplitView);

            LoadGraphIntoView();
        }

        private static VisualElement BuildNavigationHint()
        {
            var overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            overlay.style.position = Position.Absolute;
            overlay.style.left = 12;
            overlay.style.bottom = 10;
            overlay.style.alignItems = Align.FlexStart;

            var panel = new VisualElement();
            panel.AddToClassList("terrakit-navigation-hint");
            // Keep the shortcut panel above the button so the button never moves when toggled.
            panel.style.position = Position.Relative;
            panel.style.left = 0;
            panel.style.bottom = 0;
            panel.style.marginBottom = 6;
            panel.style.display = DisplayStyle.None;

            var heading = new Label("CANVAS CONTROLS");
            heading.AddToClassList("terrakit-navigation-heading");
            panel.Add(heading);

            bool mac = Application.platform == RuntimePlatform.OSXEditor;
            string alternatePan = mac
                ? "Middle-drag or Option + left-drag"
                : "Middle-drag or Alt + left-drag";

            AddNavigationHintRow(panel, "Move canvas", alternatePan);
            AddNavigationHintRow(panel, "Zoom", "Mouse wheel");
            AddNavigationHintRow(panel, "Focus node", "Select a node, then press F");
            AddNavigationHintRow(panel, "Delete", mac ? "Cmd + Delete" : "Delete");
            AddNavigationHintRow(panel, "Undo", mac ? "Cmd + Z" : "Ctrl + Z");
            AddNavigationHintRow(panel, "Redo", mac ? "Cmd + Shift + Z" : "Ctrl + Y");

            bool expanded = false;
            var toggle = new Button { tooltip = "Show canvas controls" };
            toggle.style.width = 96;
            toggle.style.height = 24;
            toggle.style.flexDirection = FlexDirection.Row;
            toggle.style.alignItems = Align.Center;
            toggle.style.justifyContent = Justify.Center;
            toggle.style.marginLeft = 0;
            toggle.style.marginRight = 0;
            toggle.style.marginTop = 0;
            toggle.style.marginBottom = 0;

            // Draw the circle explicitly so the icon does not depend on a special font glyph.
            var helpIcon = new Label("?") { pickingMode = PickingMode.Ignore };
            helpIcon.style.width = helpIcon.style.height = 14;
            helpIcon.style.flexShrink = 0;
            helpIcon.style.marginLeft = helpIcon.style.marginTop = helpIcon.style.marginBottom = 0;
            helpIcon.style.marginRight = 5;
            helpIcon.style.paddingLeft = helpIcon.style.paddingRight = 0;
            helpIcon.style.paddingTop = helpIcon.style.paddingBottom = 0;
            helpIcon.style.fontSize = 11;
            helpIcon.style.unityTextAlign = TextAnchor.MiddleCenter;
            helpIcon.style.borderTopWidth = helpIcon.style.borderBottomWidth = 1;
            helpIcon.style.borderLeftWidth = helpIcon.style.borderRightWidth = 1;
            helpIcon.style.borderTopLeftRadius = helpIcon.style.borderTopRightRadius = 7;
            helpIcon.style.borderBottomLeftRadius = helpIcon.style.borderBottomRightRadius = 7;
            Color iconColor = EditorGUIUtility.isProSkin
                ? new Color(0.88f, 0.88f, 0.88f) : new Color(0.2f, 0.2f, 0.2f);
            helpIcon.style.color = iconColor;
            helpIcon.style.borderTopColor = helpIcon.style.borderBottomColor = iconColor;
            helpIcon.style.borderLeftColor = helpIcon.style.borderRightColor = iconColor;
            toggle.Add(helpIcon);
            toggle.Add(new Label("Controls") { pickingMode = PickingMode.Ignore });

            toggle.clicked += () =>
            {
                expanded = !expanded;
                panel.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                toggle.tooltip = expanded ? "Hide canvas controls" : "Show canvas controls";
            };

            overlay.Add(panel);
            overlay.Add(toggle);
            return overlay;
        }

        private static void AddNavigationHintRow(VisualElement panel, string action, string control)
        {
            var row = new VisualElement();
            row.AddToClassList("terrakit-navigation-row");

            var actionLabel = new Label(action);
            actionLabel.AddToClassList("terrakit-navigation-action");

            var controlLabel = new Label(control);
            controlLabel.AddToClassList("terrakit-navigation-control");

            row.Add(actionLabel);
            row.Add(controlLabel);
            panel.Add(row);
        }

        private void BuildConsole()
        {
            _statusPanel = new VisualElement();
            _statusPanel.AddToClassList("terrakit-status-panel");

            var header = new VisualElement();
            header.AddToClassList("terrakit-status-header");

            _statusIcon = new Label();
            _statusIcon.AddToClassList("terrakit-status-icon");

            _statusType = new Label();
            _statusType.AddToClassList("terrakit-status-type");

            _statusTime = new Label();
            _statusTime.AddToClassList("terrakit-status-time");

            var clearButton = new Button(ClearStatus) { text = "Clear" };
            clearButton.AddToClassList("terrakit-status-clear");

            header.Add(_statusIcon);
            header.Add(_statusType);
            header.Add(_statusTime);
            header.Add(clearButton);

            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.AddToClassList("terrakit-status-scroll");

            _statusSummary = new Label();
            _statusSummary.AddToClassList("terrakit-status-summary");

            _statusDetails = new VisualElement();
            _statusDetails.AddToClassList("terrakit-status-details");

            scrollView.Add(_statusSummary);
            scrollView.Add(_statusDetails);

            _statusPanel.Add(header);
            _statusPanel.Add(scrollView);

            if (_bodySplitView != null)
            {
                _bodySplitView.Add(_statusPanel);
            }
            else
            {
                rootVisualElement.Add(_statusPanel);
            }

            ClearStatus();
        }

        private void LoadStylesheet()
        {
            // Resolve by GUID rather than a project-relative path so this works both as an embedded/local UPM package and when the source lives under Assets/.
            const string styleGuid = "c25911b1c96c042d99eecbaaa360179a";
            var stylePath = AssetDatabase.GUIDToAssetPath(styleGuid);
            var stylesheet = string.IsNullOrEmpty(stylePath)
                ? null
                : AssetDatabase.LoadAssetAtPath<StyleSheet>(stylePath);

            if (stylesheet != null)
            {
                rootVisualElement.styleSheets.Add(stylesheet);
            }
        }

        private void CreateGraphAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create TerraKit Graph",
                "TerraKitGenerationGraph",
                "asset",
                "Choose where to save the TerraKit graph asset.");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var asset = CreateInstance<TerraKitGraphAsset>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssetIfDirty(asset);
            AssetDatabase.Refresh();

            _graphAsset = asset;
            _graphField.SetValueWithoutNotify(asset);
            LoadGraphIntoView();
            SetStatus(
                StatusType.Success,
                "Graph created successfully.",
                "Asset: " + path);
        }

        private void LoadGraphIntoView(bool frameAll = true)
        {
            if (_graphView != null)
            {
                string error = null;

                bool needsBackend =
                    _graphAsset != null &&
                    _graphAsset.nodes.Count > 0;

                bool backendReady =
                    !needsBackend ||
                    TerraKitNodeRegistry.TryLoadBackend(out error);

                _graphView.Load(backendReady ? _graphAsset : null, frameAll);

                if (!backendReady)
                {
                    SetStatus(
                        StatusType.Error,
                        "Cannot load backend nodes.",
                        error +
                        "\nFix the backend connection, then restart Unity.");
                }
            }

            _selectedNode = null;
            ShowEmptyInspector();
            UpdateGraphControls();
            RefreshSaveStateFromAsset();
            RefreshGraphFieldBranding();
        }

        private void RefreshGraphFieldBranding()
        {
            if (_graphField == null)
            {
                return;
            }

            _graphField.schedule.Execute(() =>
            {
                VisualElement objectDisplay =
                    _graphField.Q<VisualElement>(
                        className: ObjectField.objectUssClassName);
                Label objectLabel =
                    objectDisplay == null
                        ? null
                        : objectDisplay.Q<Label>();
                if (objectLabel == null)
                {
                    return;
                }

                objectLabel.text = _graphAsset == null
                    ? "None (TerraKit Graph Asset)"
                    : _graphAsset.name + " (TerraKit Graph Asset)";
            });
        }

        private void HandleNodeSelected(TerraKitNodeView node)
        {
            _selectedNode = node;
            ShowNodeInspector(node);
            UpdateGraphControls();
        }

        private void HandleGraphChanged()
        {
            _graphView.SetValidationErrors(null);
            SetSavedAt(DateTime.Now);
            SetStatus(
                StatusType.Info,
                "Graph changed and saved automatically.",
                "Click Validate to check the updated graph and locate errors.");
        }

        private void RefreshSaveStateFromAsset()
        {
            if (_saveStateLabel == null)
            {
                return;
            }

            if (_graphAsset == null)
            {
                _saveStateLabel.style.display = DisplayStyle.None;
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(_graphAsset);
            if (!string.IsNullOrEmpty(assetPath))
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string fullPath = Path.Combine(projectRoot, assetPath);
                if (File.Exists(fullPath))
                {
                    SetSavedAt(File.GetLastWriteTime(fullPath));
                    return;
                }
            }

            SetSavedAt(DateTime.Now);
        }

        private void SetSavedAt(DateTime savedAt)
        {
            if (_saveStateLabel == null || _graphAsset == null)
            {
                return;
            }

            _saveStateLabel.text = "Saved " + savedAt.ToString("HH:mm:ss");
            _saveStateLabel.tooltip =
                "Changes are saved automatically.\nLast saved: " +
                savedAt.ToString("yyyy-MM-dd HH:mm:ss");
            _saveStateLabel.style.display = DisplayStyle.Flex;
        }

        private void SaveGraph()
        {
            if (_graphAsset == null)
            {
                SetStatus(
                    StatusType.Error,
                    "Graph could not be saved.",
                    "No graph is selected. Create or assign a TerraKit graph first.");
                return;
            }

            if (_graphView != null)
            {
                _graphView.SyncNodeLayoutsToAsset();
            }

            TerraKitGraphEditorUtil.MarkDirty(_graphAsset);
            SetSavedAt(DateTime.Now);
            SetStatus(
                StatusType.Success,
                "Graph saved successfully.",
                "Asset: " + AssetDatabase.GetAssetPath(_graphAsset));
        }

        private void ValidateGraph()
        {
            if (_graphAsset == null)
            {
                SetStatus(
                    StatusType.Error,
                    "Validation could not start.",
                    "No graph is selected.");
                return;
            }

            var result = new TerraKitGraphCompiler().Compile(_graphAsset);
            DisplayValidationResult(result);
        }

        private void DisplayValidationResult(TerraKitGraphCompileResult result)
        {
            _graphView.SetValidationErrors(result.ErrorIssues);

            if (!result.Success)
            {
                SetStatus(
                    StatusType.Error,
                    "Validation failed with " + result.ErrorIssues.Count + " error(s).");
            }
            else
            {
                SetStatus(
                    StatusType.Success,
                    "Validation passed. " +
                    result.ExecutionOrder.Count + " node(s) are ready.");
            }

            PopulateValidationDetails(result);

            var firstNodeError = result.ErrorIssues.FirstOrDefault(
                issue => !string.IsNullOrEmpty(issue.NodeId));

            if (firstNodeError != null)
            {
                GoToNode(firstNodeError.NodeId);
            }
        }

        private void PopulateValidationDetails(TerraKitGraphCompileResult result)
        {
            _statusDetails.Clear();
            _statusDetails.style.display = DisplayStyle.Flex;

            foreach (var issue in result.ErrorIssues)
            {
                var row = new VisualElement();
                row.AddToClassList("terrakit-status-error-row");

                var message = new Label("Error: " + issue.Message);
                message.AddToClassList("terrakit-status-detail-message");
                row.Add(message);

                if (!string.IsNullOrEmpty(issue.NodeId))
                {
                    string nodeId = issue.NodeId;

                    var goToButton = new Button(() => GoToNode(nodeId))
                    {
                        text = "Go to"
                    };

                    goToButton.AddToClassList("terrakit-status-go-to");
                    row.Add(goToButton);
                }

                _statusDetails.Add(row);
            }

            if (result.Success)
            {
                AddValidationSummary(result);

                var executionHeading = new Label("Execution order");
                executionHeading.AddToClassList("terrakit-compile-section-heading");
                _statusDetails.Add(executionHeading);

                for (int i = 0; i < result.ExecutionOrder.Count; i++)
                {
                    AddStatusDetailLabel(
                        (i + 1) + ". " + result.ExecutionOrder[i].DisplayLabel);
                }
            }

            _statusDetails.style.display = _statusDetails.childCount == 0
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        private void AddValidationSummary(TerraKitGraphCompileResult result)
        {
            int inputCount = 0;
            int outputCount = 0;

            foreach (var node in _graphAsset.nodes)
            {
                ITerraKitNodeDefinition definition;
                if (!TerraKitNodeRegistry.TryGet(node.typeId, out definition))
                {
                    continue;
                }

                if (definition.Inputs.Count == 0 && !TerraKitNodeRegistry.IsOutput(definition))
                {
                    inputCount++;
                }
                else if (TerraKitNodeRegistry.IsOutput(definition))
                {
                    outputCount++;
                }
            }

            int processorCount = _graphAsset.nodes.Count - inputCount - outputCount;

            var summary = new VisualElement();
            summary.AddToClassList("terrakit-compile-summary");

            var heading = new Label("Graph summary");
            heading.AddToClassList("terrakit-compile-section-heading");
            summary.Add(heading);

            var metrics = new VisualElement();
            metrics.AddToClassList("terrakit-compile-metrics");
            AddValidationMetric(metrics, "Nodes", _graphAsset.nodes.Count);
            AddValidationMetric(metrics, "Connections", _graphAsset.edges.Count);
            AddValidationMetric(metrics, "Sources", inputCount);
            AddValidationMetric(metrics, "Processors", processorCount);
            AddValidationMetric(metrics, "Outputs", outputCount);
            AddValidationMetric(metrics, "Execution steps", result.ExecutionOrder.Count);
            summary.Add(metrics);

            _statusDetails.Add(summary);
        }

        private static void AddValidationMetric(VisualElement parent, string name, int value)
        {
            var metric = new VisualElement();
            metric.AddToClassList("terrakit-compile-metric");

            var valueLabel = new Label(value.ToString());
            valueLabel.AddToClassList("terrakit-compile-metric-value");

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("terrakit-compile-metric-name");

            metric.Add(valueLabel);
            metric.Add(nameLabel);
            parent.Add(metric);
        }

        private void GoToNode(string nodeId)
        {
            if (_graphView != null)
            {
                _graphView.GoToNode(nodeId);
            }
        }

        private void AddStatusDetailLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("terrakit-status-detail-message");
            _statusDetails.Add(label);
        }

        private void CreateBackendDemoGraph()
        {
            const string demoFolder = "Assets/TerraKitGraphs";
            if (!AssetDatabase.IsValidFolder(demoFolder))
            {
                AssetDatabase.CreateFolder("Assets", "TerraKitGraphs");
            }

            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                demoFolder + "/TerraKitBackendDemo.asset");

            var demoAsset = CreateInstance<TerraKitGraphAsset>();
            AssetDatabase.CreateAsset(demoAsset, assetPath);

            _graphAsset = demoAsset;
            _graphField.SetValueWithoutNotify(_graphAsset);

            var flat = CreateNode("terrakit.height.flat", new Vector2(50, 160));
            var noise = CreateNode("terrakit.height.noise", new Vector2(390, 130));
            var mesh = CreateNode("terrakit.mesh.height_field", new Vector2(730, 160));

            SetDemoParameter(noise, "frequency", "0.08");
            SetDemoParameter(noise, "amplitude", "10");

            AddEdge(flat, "height", noise, "source");
            AddEdge(noise, "height", mesh, "height");

            TerraKitGraphEditorUtil.MarkDirty(_graphAsset);

            LoadGraphIntoView();
            ValidateGraph();

            Selection.activeObject = _graphAsset;
            EditorGUIUtility.PingObject(_graphAsset);
        }

        private static void SetDemoParameter(
            TerraKitNodeData node,
            string parameterKey,
            string value)
        {
            if (node == null)
            {
                return;
            }

            var parameter = node.parameters.FirstOrDefault(
                item => item.key == parameterKey);
            if (parameter != null)
            {
                parameter.value = value;
            }
        }

        private TerraKitNodeData CreateNode(string typeId, Vector2 position)
        {
            ITerraKitNodeDefinition definition;
            if (!TerraKitNodeRegistry.TryGet(typeId, out definition))
            {
                Debug.LogError("Unknown TerraKit node type: " + typeId);
                return null;
            }

            var node = TerraKitGraphEditorUtil.CreateNodeData(definition, position);
            _graphAsset.nodes.Add(node);
            return node;
        }

        private void AddEdge(TerraKitNodeData outputNode, string outputPort, TerraKitNodeData inputNode, string inputPort)
        {
            if (outputNode == null || inputNode == null)
            {
                return;
            }

            _graphAsset.edges.Add(new TerraKitEdgeData
            {
                id = System.Guid.NewGuid().ToString("N"),
                outputNodeId = outputNode.id,
                outputPortId = outputPort,
                inputNodeId = inputNode.id,
                inputPortId = inputPort
            });
        }

        private void ShowEmptyInspector()
        {
            if (_inspector == null)
            {
                return;
            }

            _inspector.Clear();
            _inspector.Add(new Label("Inspector") { name = "InspectorTitle" });

            var guidance = new Label(
                "Select a node to edit its parameters. Right-click the canvas to add nodes.");
            guidance.AddToClassList("terrakit-inspector-text");
            _inspector.Add(guidance);
        }

        private void ShowNodeInspector(TerraKitNodeView node)
        {
            if (_inspector == null)
            {
                return;
            }

            _inspector.Clear();

            if (node == null)
            {
                ShowEmptyInspector();
                return;
            }

            _inspector.Add(new Label(node.Data.DisplayLabel) { name = "InspectorTitle" });

            var typeLabel = new Label(node.Definition.Category + " / " + node.Definition.TypeId);
            typeLabel.AddToClassList("terrakit-inspector-text");
            _inspector.Add(typeLabel);

            var idLabel = new Label("Node ID: " + node.Data.id);
            idLabel.AddToClassList("terrakit-inspector-text");
            _inspector.Add(idLabel);

            if (node.Definition.Parameters.Count == 0)
            {
                var emptyMessage = new Label("This node has no editable parameters.");
                emptyMessage.AddToClassList("terrakit-inspector-text");
                _inspector.Add(emptyMessage);
                return;
            }

            _inspector.Add(new Label("Parameters") { name = "InspectorSection" });

            foreach (var parameterDefinition in node.Definition.Parameters)
            {
                var parameter = node.Data.parameters.FirstOrDefault(p => p.key == parameterDefinition.Key);
                if (parameter == null)
                {
                    continue;
                }

                AddParameterField(node, parameterDefinition, parameter);
            }
        }

        private void AddParameterField(
            TerraKitNodeView node,
            TerraKitParameterDefinition definition,
            TerraKitParameterData parameter)
        {
            var backendParameter = definition.BackendParameter;
            string description = definition.Description;
            string defaultValue = definition.DefaultValue;
            string validValueHint = definition.ValidValueHint;

            var fieldContainer = new VisualElement();
            fieldContainer.AddToClassList("terrakit-parameter-field");

            VisualElement inputField;
            Action<string> setInputValueWithoutNotify;
            Button resetButton = null;

            switch (parameter.type)
            {
                case TerraKitParameterType.Boolean:
                    bool boolValue;
                    bool.TryParse(parameter.value, out boolValue);
                    var boolField = new Toggle(parameter.displayName) { value = boolValue };
                    boolField.RegisterValueChangedCallback(evt =>
                    {
                        UpdateParameter(node, parameter, evt.newValue ? "true" : "false");
                        RefreshParameterValidation(definition, parameter, fieldContainer);
                        resetButton?.SetEnabled(!IsDefaultParameterValue(parameter, definition));
                    });
                    inputField = boolField;
                    setInputValueWithoutNotify = value =>
                    {
                        bool parsedValue;
                        bool.TryParse(value, out parsedValue);
                        boolField.SetValueWithoutNotify(parsedValue);
                    };
                    break;

                default:
                    if (backendParameter != null &&
                        backendParameter.Kind == TerraKitBackendParameterKind.Enum &&
                        backendParameter.EnumOptions.Count > 0)
                    {
                        var options = backendParameter.EnumOptions;
                        var choices = options.Select(option => option.Id).ToList();

                        // Preserve an old saved value instead of silently changing it.
                        if (!choices.Contains(parameter.value))
                        {
                            choices.Add(parameter.value);
                        }

                        Func<string, string> formatChoice = id =>
                        {
                            var option = options.FirstOrDefault(item => item.Id == id);

                            if (option == null)
                            {
                                return id + " (unsupported)";
                            }

                            return string.IsNullOrEmpty(option.DisplayName)
                                ? option.Id
                                : option.DisplayName;
                        };

                        var dropdown = new PopupField<string>(
                            parameter.displayName,
                            choices,
                            choices.IndexOf(parameter.value),
                            formatChoice,
                            formatChoice);

                        dropdown.RegisterValueChangedCallback(evt =>
                        {
                            UpdateParameter(node, parameter, evt.newValue);
                            RefreshParameterValidation(
                                definition, parameter, fieldContainer);

                            resetButton?.SetEnabled(
                                !IsDefaultParameterValue(parameter, definition));
                        });

                        inputField = dropdown;

                        setInputValueWithoutNotify = value =>
                        {
                            if (!choices.Contains(value))
                            {
                                choices.Add(value);
                            }

                            dropdown.SetValueWithoutNotify(value);
                        };

                        break;
                    }

                    var textField = new TextField(parameter.displayName) { value = parameter.value };
                    textField.RegisterValueChangedCallback(evt =>
                    {
                        UpdateParameter(node, parameter, evt.newValue);
                        RefreshParameterValidation(definition, parameter, fieldContainer);
                        resetButton?.SetEnabled(!IsDefaultParameterValue(parameter, definition));
                    });
                    inputField = textField;
                    setInputValueWithoutNotify = value => textField.SetValueWithoutNotify(value);
                    break;
            }

            inputField.AddToClassList("terrakit-parameter-input");
            inputField.tooltip = description +
                                 "\nValid value: " + validValueHint +
                                 "\nDefault: " + defaultValue;
            fieldContainer.Add(inputField);

            if (!string.IsNullOrEmpty(description))
            {
                var descriptionLabel = new Label(description);
                descriptionLabel.AddToClassList("terrakit-parameter-description");
                fieldContainer.Add(descriptionLabel);
            }

            var metadataRow = new VisualElement();
            metadataRow.AddToClassList("terrakit-parameter-metadata-row");

            var valueHint = new Label(
                "Valid: " + validValueHint +
                "  •  Default: " + defaultValue);
            valueHint.AddToClassList("terrakit-parameter-value-hint");
            metadataRow.Add(valueHint);

            resetButton = new Button(() =>
            {
                setInputValueWithoutNotify(defaultValue);
                UpdateParameter(node, parameter, defaultValue);
                RefreshParameterValidation(definition, parameter, fieldContainer);
                resetButton.SetEnabled(false);
            })
            {
                text = "Reset",
                tooltip = "Restore the default value: " + defaultValue
            };
            resetButton.AddToClassList("terrakit-parameter-reset");
            resetButton.SetEnabled(!IsDefaultParameterValue(parameter, definition));
            metadataRow.Add(resetButton);
            fieldContainer.Add(metadataRow);

            var errorLabel = new Label();
            errorLabel.AddToClassList("terrakit-parameter-error");
            fieldContainer.Add(errorLabel);

            _inspector.Add(fieldContainer);
            RefreshParameterValidation(definition, parameter, fieldContainer);
        }

        private static bool IsDefaultParameterValue(
            TerraKitParameterData parameter,
            TerraKitParameterDefinition definition)
        {
            bool value;
            bool defaultValue;
            if (definition.Type == TerraKitParameterType.Boolean &&
                bool.TryParse(parameter.value, out value) &&
                bool.TryParse(definition.DefaultValue, out defaultValue))
            {
                return value == defaultValue;
            }

            return parameter.value == definition.DefaultValue;
        }

        private static void RefreshParameterValidation(
            TerraKitParameterDefinition definition,
            TerraKitParameterData parameter,
            VisualElement fieldContainer)
        {
            var errorLabel = fieldContainer.Q<Label>(className: "terrakit-parameter-error");
            var issues = TerraKitParameterValidator.Validate(definition, parameter);

            if (issues.Count == 0)
            {
                fieldContainer.RemoveFromClassList("terrakit-parameter-invalid");
                errorLabel.text = string.Empty;
                errorLabel.style.display = DisplayStyle.None;
                return;
            }

            fieldContainer.AddToClassList("terrakit-parameter-invalid");
            errorLabel.text = string.Join("\n", issues.Select(issue => issue.Message));
            errorLabel.style.display = DisplayStyle.Flex;
        }

        private void UpdateParameter(TerraKitNodeView node, TerraKitParameterData parameter, string value)
        {
            if (_graphAsset == null || parameter.value == value)
            {
                return;
            }

            Undo.RecordObject(_graphAsset, "Edit TerraKit Parameter");
            parameter.value = value;
            node.RefreshParameterSummary();
            TerraKitGraphEditorUtil.MarkDirty(_graphAsset);
            HandleGraphChanged();
        }

        private void UpdateGraphControls()
        {
            bool hasGraph = _graphAsset != null;

            _saveButton.SetEnabled(hasGraph);
            _validateButton.SetEnabled(hasGraph);
            _frameAllButton.SetEnabled(hasGraph);
            _focusButton.SetEnabled(hasGraph && _selectedNode != null);
            _resetZoomButton.SetEnabled(hasGraph);
            _generatorButton.SetEnabled(hasGraph);
            _generatorButton.tooltip = "Open Backend Generator. The graph is checked before generation.";
        }

        private void FrameAllNodes()
        {
            if (_graphView == null)
            {
                return;
            }

            _graphView.FrameAll();
            SetStatus(
                StatusType.Info,
                "All nodes were fitted to the current view.",
                "You can continue editing or validating the graph.");
        }

        private void FocusSelectedNode()
        {
            if (_graphView == null || !_graphView.FocusSelection())
            {
                return;
            }

            SetStatus(
                StatusType.Info,
                "The selected node was fitted to the current view.",
                "Use the mouse wheel to zoom, or click 100% to reset zoom.");
        }

        private void ResetZoom()
        {
            if (_graphView == null)
            {
                return;
            }

            _graphView.ResetZoomTo100Percent();
            SetStatus(
                StatusType.Info,
                "Canvas zoom was reset to 100%.",
                "The current canvas centre was preserved.");
        }

        private void ShowHelp()
        {
            SetStatus(
                StatusType.Info,
                "TerraKit — Quick Start",
                "Build a graph, generate terrain, preview the results, and save or export what you need.\n\n" +

                "1. Check the backend connection: Tools > TerraKit > Check Backend Connection.\n\n" +

                "2. Click New Graph. Right-click the canvas to add stages, then connect each output to the next stage's required input.\n\n" +

                "3. Select a node to edit its parameters in the Inspector. Click Save to save the graph, and use Validate to check for missing inputs or invalid settings.\n\n" +

                "4. Click Generator. Choose the generation settings, then click Generate Map.\n\n" +

                "5. In Results, use Region and Output to inspect generated resources. HeightField outputs are shown in Data Preview. " +
                "For Mesh outputs, click Show Map Preview to view the complete map.\n\n" +

                "6. Save Mesh saves all Mesh outputs and materials in the selected Region, with a Region Prefab that preserves material assignments. " +
                "Save Prefab saves all Regions and their materials as one reusable map Prefab.\n\n" +

                "7. Export JSON saves the generated data and request settings. " +
                "Export PNG saves an image of the complete map. Heightmap and Slice Image export the selected grid data as PNG.\n\n" +

                "Regions X/Y controls the map size in regions. Regions are arranged automatically around the Unity origin; " +
                "set both to 1 for a single region.\n\n" +

                "LOD controls detail level. Cell Width/Height controls the region resolution, and Base Spacing controls the distance between samples.\n\n" +

                "3D mode requires compatible 3D stages. The current built-in HeightField stages are designed for 2D generation.\n\n" +

                "Scene previews are temporary. Save the result if you want to keep it."
            );
        }

        private void ClearStatus()
        {
            SetStatus(
                StatusType.Info,
                "Ready.",
                _graphAsset == null
                    ? "Select or create a TerraKit graph to begin."
                    : "Select a node, edit its parameters, or choose an action from the toolbar.");
        }

        private void SetStatus(StatusType type, string summary, string details = "")
        {
            if (_statusPanel == null)
            {
                return;
            }

            _statusPanel.RemoveFromClassList("terrakit-status-info");
            _statusPanel.RemoveFromClassList("terrakit-status-success");
            _statusPanel.RemoveFromClassList("terrakit-status-warning");
            _statusPanel.RemoveFromClassList("terrakit-status-error");

            string icon;
            string typeName;
            string styleClass;

            switch (type)
            {
                case StatusType.Success:
                    icon = "✓";
                    typeName = "SUCCESS";
                    styleClass = "terrakit-status-success";
                    break;
                case StatusType.Warning:
                    icon = "⚠";
                    typeName = "WARNING";
                    styleClass = "terrakit-status-warning";
                    break;
                case StatusType.Error:
                    icon = "✕";
                    typeName = "ERROR";
                    styleClass = "terrakit-status-error";
                    break;
                default:
                    icon = "ⓘ";
                    typeName = "INFO";
                    styleClass = "terrakit-status-info";
                    break;
            }

            _statusPanel.AddToClassList(styleClass);
            _statusIcon.text = icon;
            _statusType.text = typeName;
            _statusTime.text = System.DateTime.Now.ToString("HH:mm:ss");
            _statusSummary.text = summary;
            _statusDetails.Clear();
            if (!string.IsNullOrEmpty(details))
            {
                AddStatusDetailLabel(details);
            }
            _statusDetails.style.display = string.IsNullOrEmpty(details)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }
    }
}
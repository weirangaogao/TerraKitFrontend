using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace TerraKit.Editor
{
    internal sealed class TerraKitNodeView : Node
    {
        private static readonly Vector2 MinimumSize = new Vector2(220, 120);

        private readonly Dictionary<string, Port> _inputPorts = new Dictionary<string, Port>();
        private readonly Dictionary<string, Port> _outputPorts = new Dictionary<string, Port>();
        private readonly Dictionary<string, Label> _parameterValueLabels = new Dictionary<string, Label>();
        private readonly Label _errorBadge;
        private readonly TerraKitGraphAsset _graphAsset;

        public TerraKitNodeData Data { get; private set; }
        public ITerraKitNodeDefinition Definition { get; private set; }
        public event Action Resized;

        public TerraKitNodeView(
            TerraKitNodeData data, ITerraKitNodeDefinition definition,
            TerraKitGraphAsset graphAsset)
        {
            _graphAsset = graphAsset;
            Data = data;
            Definition = definition;

            viewDataKey = data.id;
            title = data.DisplayLabel;
            expanded = true;
            titleButtonContainer.RemoveFromHierarchy();

            capabilities |= Capabilities.Movable |
                            Capabilities.Selectable |
                            Capabilities.Deletable |
                            Capabilities.Resizable;

            style.minWidth = MinimumSize.x;
            style.minHeight = MinimumSize.y;
            AddToClassList("terrakit-node");
            AddToClassList("terrakit-node-" + definition.Category.ToLowerInvariant());

            titleContainer.AddToClassList("terrakit-node-title");
            inputContainer.AddToClassList("terrakit-node-inputs");
            outputContainer.AddToClassList("terrakit-node-outputs");
            extensionContainer.AddToClassList("terrakit-node-extension");

            var categoryBadge = new Label(definition.Category.ToUpperInvariant());
            categoryBadge.AddToClassList("terrakit-node-category");
            titleContainer.Add(categoryBadge);

            _errorBadge = new Label("!");
            _errorBadge.AddToClassList("terrakit-node-error-badge");
            titleContainer.Add(_errorBadge);

            TerraKitGraphEditorUtil.EnsureParameterDefaults(data, definition);

            foreach (var input in definition.Inputs)
            {
                var port = CreatePort(input, Direction.Input);
                _inputPorts[input.Id] = port;
                inputContainer.Add(port);
            }

            foreach (var output in definition.Outputs)
            {
                var port = CreatePort(output, Direction.Output);
                _outputPorts[output.Id] = port;
                outputContainer.Add(port);
            }

            BuildParameterSummary();

            bool hasSavedSize = data.size.x >= MinimumSize.x && data.size.y >= MinimumSize.y;
            style.width = hasSavedSize ? data.size.x : CalculateInitialWidth();
            style.height = hasSavedSize ? new StyleLength(data.size.y) : StyleKeyword.Auto;
            style.overflow = Overflow.Hidden;
            SetPosition(new Rect(data.position, data.size));
            if (!hasSavedSize) RegisterCallback<GeometryChangedEvent>(CaptureInitialSize);

            var resizer = new Resizer(MinimumSize, OnResized);
            resizer.AddToClassList("terrakit-node-resizer");
            hierarchy.Add(resizer);

            RefreshExpandedState();
            RefreshPorts();
            ConfigureContentLayout();
        }

        public Port GetInputPort(string portId)
        {
            Port port;
            return _inputPorts.TryGetValue(portId, out port) ? port : null;
        }

        public Port GetOutputPort(string portId)
        {
            Port port;
            return _outputPorts.TryGetValue(portId, out port) ? port : null;
        }

        public void RefreshParameterSummary()
        {
            foreach (var parameter in Data.parameters)
            {
                Label valueLabel;
                if (_parameterValueLabels.TryGetValue(parameter.key, out valueLabel))
                {
                    valueLabel.text = parameter.value;
                }
            }
        }

        public void SetValidationErrorCount(int errorCount)
        {
            bool hasErrors = errorCount > 0;
            EnableInClassList("terrakit-node-error", hasErrors);
            _errorBadge.text = errorCount > 1 ? "! " + errorCount : "!";
            _errorBadge.style.display = hasErrors ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void CaptureCurrentLayout()
        {
            var currentPosition = GetPosition();
            var currentSize = currentPosition.size;

            if (float.IsNaN(currentSize.x) || float.IsNaN(currentSize.y) ||
                float.IsInfinity(currentSize.x) || float.IsInfinity(currentSize.y) ||
                currentSize.x < MinimumSize.x || currentSize.y < MinimumSize.y)
            {
                currentSize = Data.size;
            }

            if (Data.position == currentPosition.position && Data.size == currentSize)
            {
                return;
            }

            Undo.RecordObject(_graphAsset, "Change TerraKit Node Layout");
            Data.position = currentPosition.position;
            Data.size = currentSize;
        }

        private Port CreatePort(TerraKitPortDefinition definition, Direction direction)
        {
            var capacity = definition.AllowMultipleConnections ? Port.Capacity.Multi : Port.Capacity.Single;
            var port = InstantiatePort(Orientation.Horizontal, direction, capacity, typeof(TerraKitPortType));
            port.portName = definition.DisplayName + (definition.IsRequired ? " *" : string.Empty);
            port.tooltip = definition.DisplayName + "\nType: " + definition.Type +
                           (definition.IsRequired ? "\nRequired input" : string.Empty);
            port.userData = new TerraKitEditorPortBinding(Data.id, definition.Id, definition.Type, direction);
            port.AddToClassList("terrakit-port");
            port.AddToClassList(direction == Direction.Input ? "terrakit-port-input" : "terrakit-port-output");
            port.AddToClassList("terrakit-port-" + definition.Type.ToString().ToLowerInvariant());
            return port;
        }

        private void BuildParameterSummary()
        {
            if (Definition.Parameters.Count == 0)
            {
                return;
            }

            var heading = new Label("PARAMETERS");
            heading.AddToClassList("terrakit-node-parameter-heading");
            extensionContainer.Add(heading);

            foreach (var definition in Definition.Parameters)
            {
                var parameter = Data.parameters.FirstOrDefault(item => item.key == definition.Key);
                if (parameter == null)
                {
                    continue;
                }

                var row = new VisualElement();
                row.AddToClassList("terrakit-node-parameter-row");
                row.style.flexShrink = 0;

                var nameLabel = new Label(parameter.displayName);
                nameLabel.AddToClassList("terrakit-node-parameter-name");
                nameLabel.style.whiteSpace = WhiteSpace.Normal;

                var valueLabel = new Label(parameter.value);
                valueLabel.AddToClassList("terrakit-node-parameter-value");
                valueLabel.style.flexBasis = 0;
                valueLabel.style.whiteSpace = WhiteSpace.Normal;
                valueLabel.style.textOverflow = TextOverflow.Clip;

                row.Add(nameLabel);
                row.Add(valueLabel);
                extensionContainer.Add(row);
                _parameterValueLabels[parameter.key] = valueLabel;
            }
        }

        private void ConfigureContentLayout()
        {
            // Keep rows readable; manually smaller nodes clip content without growing back.
            for (var element = extensionContainer; element != null && element != this; element = element.parent)
                element.style.flexShrink = 0;
            titleContainer.style.flexShrink = 0;
            topContainer.style.flexShrink = 0;

            var titleLabel = titleContainer.Q<Label>("title-label");
            if (titleLabel != null)
            {
                titleLabel.style.minWidth = 0;
                titleLabel.style.whiteSpace = WhiteSpace.Normal;
                titleLabel.tooltip = title;
            }
        }

        private float CalculateInitialWidth()
        {
            var textStyle = new GUIStyle(EditorStyles.label) { fontSize = 10 };
            var valueStyle = new GUIStyle(textStyle) { fontStyle = FontStyle.Bold };
            var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            float width = Mathf.Max(260, titleStyle.CalcSize(new GUIContent(title)).x +
                textStyle.CalcSize(new GUIContent(Definition.Category.ToUpperInvariant())).x + 64);
            foreach (var parameter in Data.parameters)
            {
                width = Mathf.Max(width, 24 + (textStyle.CalcSize(new GUIContent(parameter.displayName)).x + 8) / 0.55f);
                width = Mathf.Max(width, 24 + (valueStyle.CalcSize(new GUIContent(parameter.value)).x + 8) / 0.45f);
            }
            return Mathf.Ceil(width);
        }

        private void CaptureInitialSize(GeometryChangedEvent evt)
        {
            if (!float.IsFinite(layout.width) || !float.IsFinite(layout.height) ||
                layout.width < MinimumSize.x || layout.height < MinimumSize.y) return;

            // Resolve automatic height once, then persist a freely resizable node size.
            UnregisterCallback<GeometryChangedEvent>(CaptureInitialSize);
            Data.size = layout.size;
            style.height = layout.height;
            Resized?.Invoke();
        }

        private void OnResized()
        {
            // The Resizer callback can run before UI Toolkit completes its layout pass.
            schedule.Execute(() =>
            {
                CaptureCurrentLayout();
                Resized?.Invoke();
            });
        }
    }
}
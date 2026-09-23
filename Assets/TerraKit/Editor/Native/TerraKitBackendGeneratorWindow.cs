using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TerraKit.Editor
{
    internal sealed class TerraKitBackendGeneratorWindow : EditorWindow
    {
        internal const int MaxRegionCount = 256;
        private const float RequestPanelWidth = 520f;
        private const float ResultsPanelWidth = RequestPanelWidth;
        private const float PanelGap = 14f;
        private const float ScrollViewWidthAllowance = 24f;
        private const float TwoColumnWindowWidth = RequestPanelWidth + ResultsPanelWidth
            + PanelGap + ScrollViewWidthAllowance;
        private const float DefaultWindowHeight = 860f;

        [SerializeField] private TerraKitGraphAsset _graph;
        [SerializeField] private string _regionsX = "3", _regionsY = "3", _regionsZ = "3";
        [SerializeField] private int _dimension;
        private Vector2 _scroll, _statusScroll;
        [SerializeField] private float _statusHeightRatio = 0.2f;
        private IReadOnlyList<TerraKitGeneratedRegion> _results;
        private bool _resultsStale, _isGenerating;
        private int _regionIndex, _outputIndex, _slice, _sampleX, _sampleY;
        private string _message = "Select a graph, then Generate.";
        private MessageType _messageType = MessageType.Info;
        private string _generatedGraphName, _snapshot, _generatedAt;
        private TerraKitGraphAsset _observedGraph;
        private string _observedContent, _observedRequest;
        private Texture2D _preview;
        private float _minimum, _maximum;
        private TerraKitOutputScenePreview _scene = new TerraKitOutputScenePreview();
        [SerializeField] private string[] _inputText;
        private string[] _inputErrors = new string[8];
        private static readonly string[] InputNames = { "Seed", "LOD",
            "Cell Width", "Cell Height", "Cell Depth", "Base Spacing X", "Base Spacing Y", "Base Spacing Z" };

        [Serializable]
        private sealed class GenerationContent
        {
            public string graphName;
            public List<TerraKitNodeData> nodes;
            public List<TerraKitEdgeData> edges;
        }

        private static GenerationContent CreateGenerationContent(TerraKitGraphAsset graph)
        {
            return new GenerationContent
            {
                graphName = graph.name,
                // Copy generation data without node positions or sizes; never modify the saved graph.
                nodes = graph.nodes?.Select(node => node == null ? null : new TerraKitNodeData
                {
                    id = node.id,
                    typeId = node.typeId,
                    schemaVersion = node.schemaVersion,
                    displayName = node.displayName,
                    parameters = node.parameters
                }).ToList(),
                edges = graph.edges
            };
        }

        private void ObserveChanges()
        {
            if (_isGenerating) return;
            EnsureInputText();
            string content = _graph == null ? null : JsonUtility.ToJson(CreateGenerationContent(_graph));
            string request = _dimension + ":" + string.Join("\n", _inputText) + ":" + _regionsX + ":" + _regionsY + (_dimension == 1 ? ":" + _regionsZ : "");
            bool graphChanged = _graph != _observedGraph || content != _observedContent;
            if (!graphChanged && request == _observedRequest) return;
            bool first = _observedRequest == null || _graph != _observedGraph;
            _observedGraph = _graph;
            _observedContent = content;
            _observedRequest = request;
            _resultsStale = _results != null;
            _message = _graph == null ? "Select a graph."
                : first ? "Click Generate Map."
                : graphChanged ? "Graph changed. Generate again."
                : "Settings changed. Generate again.";
            _messageType = MessageType.Info;
        }

        private void ClearResults()
        {
            ClearTexture();
            _scene.Dispose();
            _results = null;
            _resultsStale = false;
            _generatedGraphName = _snapshot = _generatedAt = null;
            _regionIndex = _outputIndex = _slice = _sampleX = _sampleY = 0;
        }

        internal static void OpenForGraph(TerraKitGraphAsset graph)
        {
            bool wasOpen = HasOpenInstances<TerraKitBackendGeneratorWindow>();
            var window = GetWindow<TerraKitBackendGeneratorWindow>(
                "Backend Generator");

            window.minSize = new Vector2(ResultsPanelWidth + ScrollViewWidthAllowance, 650);

            if (!wasOpen && !window.docked)
            {
                window.maximized = false;
                var rect = window.position;
                // Fit both fixed-width panels and leave room for the vertical scrollbar.
                rect.size = new Vector2(TwoColumnWindowWidth, DefaultWindowHeight);
                window.position = rect;
            }

            if (graph != null)
                window._graph = graph;

            window.Show();
        }

        private void OnDisable() { ClearResults(); }
        private void OnInspectorUpdate() { ObserveChanges(); Repaint(); }
        private void ClearTexture() { if (_preview != null) DestroyImmediate(_preview); _preview = null; }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), CanvasColor);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.Space(8);

            bool split = position.width >= TwoColumnWindowWidth;
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 150;
            if (split)
            {
                EditorGUILayout.BeginHorizontal();
            }
            EditorGUILayout.BeginVertical(GUILayout.Width(Mathf.Min(RequestPanelWidth,
                position.width - ScrollViewWidthAllowance)), GUILayout.ExpandWidth(false));

            bool ready;
            string warnings;
            using (var panel = new EditorGUILayout.VerticalScope(ContentPanelStyle))
            {
                DrawContentPanelBackground(panel.rect);
                DrawRequest();
                EditorGUILayout.Space(8);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (ActionButton("Reset Request Settings", height: 38, inline: true))
                        RunAction(() =>
                        {
                            _dimension = 0;
                            _inputText = null;
                            _regionsX = _regionsY = _regionsZ = "3";
                            GUI.FocusControl(null);
                            ObserveChanges();
                            return true;
                        }, "Request settings reset successfully.");
                    ObserveChanges();
                    string validation;
                    TerraKitBackendGenerationRequest request;
                    bool valid = TryRequest(out request, out validation);
                    int width, height, depth = 1;
                    bool validX = TryRegionCount(_regionsX, out width);
                    bool validY = TryRegionCount(_regionsY, out height);
                    bool validZ = _dimension == 0 || TryRegionCount(_regionsZ, out depth);
                    string layoutError = null;
                    if (validX && validY && validZ)
                    {
                        layoutError = RegionCountError(width, height, depth);
                        if (valid && layoutError == null)
                        {
                            try { ValidateRegionLayout(request, width, height, depth); }
                            catch (Exception exception) when (exception is ArgumentException ||
                                exception is InvalidOperationException || exception is OverflowException)
                            { layoutError = exception.Message; }
                        }
                    }
                    ready = valid && validX && validY && validZ && layoutError == null;

                    warnings = string.Join("\n", new[]
                    {
                        valid ? null : validation,
                        validX ? null : "Regions X: Enter a positive integer.",
                        validY ? null : "Regions Y: Enter a positive integer.",
                        validZ ? null : "Regions Z: Enter a positive integer.",
                        layoutError
                    }.Where(text => !string.IsNullOrWhiteSpace(text)));

                    GUILayout.Space(8);
                    using (new EditorGUI.DisabledScope(!ready))
                        if (ActionButton("Generate Map", true, inline: true))
                            Generate(request, width, height, depth);
                    GUILayout.FlexibleSpace();
                }
            }
            EditorGUILayout.EndVertical();

            if (split) GUILayout.Space(PanelGap);
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(ResultsPanelWidth),
                GUILayout.ExpandWidth(false)))
            {
                if (_results != null) DrawResults();
            }
            if (split)
            {
                // Extra window width stays outside the panels instead of stretching Results.
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            EditorGUIUtility.labelWidth = labelWidth;
            EditorGUILayout.Space(10);
            EditorGUILayout.EndScrollView();

            DrawStatusPanel(ready ? _message : warnings,
                ready ? _messageType : MessageType.Warning);
        }

        private static Color CanvasColor
        {
            get { return EditorGUIUtility.isProSkin
                ? new Color32(32, 32, 32, 255) : new Color32(235, 235, 235, 255); }
        }

        private static GUIStyle ContentPanelStyle
        {
            get { return new GUIStyle { padding = new RectOffset(8, 8, 8, 8) }; }
        }

        private static void DrawContentPanelBackground(Rect rect)
        {
            bool dark = EditorGUIUtility.isProSkin;
            EditorGUI.DrawRect(rect, dark
                ? new Color32(45, 45, 45, 255) : new Color32(180, 185, 192, 255));
            Rect fill = new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2);
            EditorGUI.DrawRect(fill, dark
                ? new Color32(56, 56, 56, 255)
                : new Color32(245, 245, 245, 255));
        }

        private static bool ActionButton(string text, bool primary = false, float height = 26, bool inline = false, string tooltip = null)
        {
            var style = new GUIStyle(GUI.skin.button)
            {
                fontStyle = primary ? FontStyle.Bold : FontStyle.Normal,
                fontSize = primary ? 13 : GUI.skin.button.fontSize,
                padding = new RectOffset(12, 12, 4, 4)
            };
            Color previous = GUI.backgroundColor;
            try
            {
                if (primary && GUI.enabled)
                    GUI.backgroundColor = new Color(0.35f, 0.68f, 1f);
                using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandWidth(!inline)))
                {
                    bool clicked = GUILayout.Button(new GUIContent(text, tooltip), style,
                        GUILayout.MinWidth(primary ? 220 : 0),
                        GUILayout.ExpandWidth(false), GUILayout.Height(primary ? 38 : height));
                    if (!inline) GUILayout.FlexibleSpace();
                    return clicked;
                }
            }
            finally { GUI.backgroundColor = previous; }
        }


        private void DrawStatusPanel(string message, MessageType type)
        {
            bool dark = EditorGUIUtility.isProSkin;
            bool error = type == MessageType.Error;
            bool warning = type == MessageType.Warning;
            float height = Mathf.Clamp(position.height * _statusHeightRatio,
                80, position.height * 0.6f);
            Rect splitter = GUILayoutUtility.GetRect(0, 6, GUILayout.ExpandWidth(true));
            EditorGUIUtility.AddCursorRect(splitter, MouseCursor.ResizeVertical);
            EditorGUI.DrawRect(splitter, dark
                ? new Color(0.16f, 0.18f, 0.2f)
                : new Color(0.65f, 0.67f, 0.69f));

            int control = GUIUtility.GetControlID(
                "TerraKitGeneratorStatusSplitter".GetHashCode(), FocusType.Passive);
            Event current = Event.current;
            switch (current.GetTypeForControl(control))
            {
                case EventType.MouseDown:
                    if (current.button == 0 && splitter.Contains(current.mousePosition))
                    {
                        GUIUtility.hotControl = control;
                        current.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == control)
                    {
                        _statusHeightRatio = Mathf.Clamp(height - current.delta.y,
                            80, position.height * 0.6f) / position.height;
                        current.Use();
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (current.button == 0 && GUIUtility.hotControl == control)
                    {
                        GUIUtility.hotControl = 0;
                        current.Use();
                    }
                    break;
            }

            Rect panel = EditorGUILayout.BeginVertical(GUILayout.Height(height));
            EditorGUI.DrawRect(panel, dark
                ? new Color32(17, 20, 24, 255) : new Color32(245, 246, 248, 255));
            Rect header = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(header, dark
                ? new Color32(24, 28, 34, 255)
                : new Color(0.82f, 0.84f, 0.86f));
            var heading = EditorStyles.boldLabel;
            GUI.Label(new Rect(header.x + 12, header.y, header.width - 24, header.height),
                error ? "ERROR" : warning ? "WARNING" : "INFO", heading);

            _statusScroll = EditorGUILayout.BeginScrollView(_statusScroll);
            var body = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                padding = new RectOffset(12, 12, 8, 8)
            };
            GUILayout.Label(message ?? "", body);
            DrawOutputIdentifiers();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }


        private void DrawOutputIdentifiers()
        {
            if (_results == null || _regionIndex < 0 || _regionIndex >= _results.Count) return;
            var resources = CurrentRegion.Resources;
            if (_outputIndex < 0 || _outputIndex >= resources.Count) return;
            var resource = resources[_outputIndex];
            var output = resource.Output;
            var style = new GUIStyle { padding = new RectOffset(12, 12, 0, 8) };
            using (new EditorGUILayout.VerticalScope(style, GUILayout.MaxWidth(600)))
            {
                DrawIdentifier("Selected Node ID", output.NodeId);
                DrawIdentifier("Port ID", output.PortId);
            }
        }

        private static void DrawIdentifier(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, GUILayout.Width(120));
                var style = new GUIStyle(EditorStyles.wordWrappedLabel);
                style.hover.textColor = style.active.textColor =
                    style.focused.textColor = style.normal.textColor;
                GUILayout.Label(value ?? "", style, GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
            }
        }

        private void DrawRequest()
        {
            EditorGUILayout.LabelField("1  Graph & request", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(new GUIContent("Graph", "The pipeline graph to run."), GUILayout.Width(130));
                _graph = (TerraKitGraphAsset)EditorGUILayout.ObjectField(
                    _graph, typeof(TerraKitGraphAsset), false);
            }
            using (new EditorGUI.DisabledScope(!(Selection.activeObject is TerraKitGraphAsset)))
                if (ActionButton("Use Project Selection", height: 22))
                    _graph = Selection.activeObject as TerraKitGraphAsset;

            DrawParameterGroup("Generation");
            DrawRegionMode();
            EnsureInputText();
            TerraKitBackendGenerationRequest unused;
            _inputErrors = ValidateInputs(_inputText, _dimension == 1, out unused);
            DrawInput(0);
            if (_dimension == 1)
                EditorGUILayout.HelpBox(
                    "3D requires compatible stages. Built-in height stages support 2D only.",
                    MessageType.Info);

            int axes = _dimension == 1 ? 3 : 2;
            DrawParameterGroup("Region layout");
            DrawInput(1);
            for (int axis = 0; axis < axes; axis++) DrawInput(2 + axis);
            for (int axis = 0; axis < axes; axis++) DrawInput(5 + axis);
            DrawParameterNote("Cell sizes must be multiples of 2^LOD.");

            DrawParameterGroup("Region count");
            DrawRegionCount("Regions X", ref _regionsX);
            DrawRegionCount(_dimension == 0 ? "Regions Y (world Z)" : "Regions Y", ref _regionsY);
            if (_dimension == 1) DrawRegionCount("Regions Z", ref _regionsZ);
            DrawParameterNote("Maximum " + MaxRegionCount + " regions in total. Regions are arranged around (0, 0, 0). Preview and Prefab use the same layout.");

        }

        private void DrawRegionMode()
        {
            using (var row = new EditorGUILayout.HorizontalScope())
            {
                Rect labelRect = GUILayoutUtility.GetRect(130, 24, GUILayout.Width(130));
                var style = new GUIStyle(EditorStyles.label);
                style.hover.textColor = style.active.textColor =
                    style.focused.textColor = style.normal.textColor;
                GUI.Label(labelRect, "Region mode", style);
                _dimension = EditorGUILayout.Popup(_dimension, new[] { "2D", "3D" },
                    GUILayout.Width(120), GUILayout.Height(24));
                GUILayout.Label(_dimension == 0 ? "Surface regions" : "Volume regions",
                    EditorStyles.wordWrappedMiniLabel,
                    GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
                GUI.Label(row.rect, new GUIContent("",
                    "Choose surface (2D) or volume (3D) generation."), GUIStyle.none);
            }
            EditorGUILayout.Space(2);
        }

        private static void DrawParameterNote(string message)
        {
            var style = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            Color color = EditorGUIUtility.isProSkin
                ? new Color(0.9f, 0.9f, 0.9f) : EditorStyles.label.normal.textColor;
            style.normal.textColor = style.hover.textColor =
                style.active.textColor = style.focused.textColor = color;
            EditorGUILayout.LabelField(message, style);
        }

        private string InputTooltip(int index)
        {
            if (index == 0) return "Controls randomness. Reuse this seed to repeat the same request.";
            if (index == 1) return "Detail level. Higher values use fewer samples.";
            int axisIndex = index <= 4 ? index - 2 : index - 5;
            string axis = _dimension == 0 && axisIndex == 1
                ? "world Z" : "XYZ"[axisIndex].ToString();
            if (index <= 4) return "Base cell count per region along " + axis + ".";
            return "Distance between base grid points along " + axis + ".";
        }


        private static void DrawParameterGroup(string title)
        {
            EditorGUILayout.Space(10);
            Rect divider = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(divider, EditorGUIUtility.isProSkin
                ? new Color(0.33f, 0.33f, 0.33f)
                : new Color(0.65f, 0.65f, 0.65f));
            EditorGUILayout.Space(6);
            Color color = EditorGUIUtility.isProSkin
                ? new Color(0.48f, 0.77f, 0.95f)
                : new Color(0.12f, 0.34f, 0.52f);
            var style = new GUIStyle(EditorStyles.boldLabel);
            style.normal.textColor = style.hover.textColor =
                style.active.textColor = style.focused.textColor = color;
            EditorGUILayout.LabelField(title, style);
            EditorGUILayout.Space(2);
        }

        private void EnsureInputText()
        {
            _inputText = NormalizeInputText(_inputText);
        }

        internal static string[] NormalizeInputText(string[] text)
        {
            if (text != null && text.Length == 8) return text;
            // Preserve existing settings while discarding the removed coordinate inputs.
            if (text != null && text.Length == 11)
                return new[] { text[0], text[4], text[5], text[6], text[7], text[8], text[9], text[10] };
            return new[] { "12345", "0", "16", "16", "16", "1", "1", "1" };
        }

        private static string InputHint(int index)
        {
            if (index == 0) return "Integer: 0 to 2^64 - 1";
            if (index == 1) return "Integer: 0–30";
            if (index <= 4) return "Integer: 1–4096";
            return "Finite number > 0";
        }

        private void DrawInput(int index)
        {
            using (var row = new EditorGUILayout.HorizontalScope())
            {
                Rect labelRect = GUILayoutUtility.GetRect(130, 24, GUILayout.Width(130));
                EditorGUI.BeginChangeCheck();
                _inputText[index] = EditorGUILayout.TextField(_inputText[index] ?? "",
                    GUILayout.Width(120), GUILayout.Height(24));
                if (EditorGUI.EndChangeCheck())
                {
                    TerraKitBackendGenerationRequest unused;
                    _inputErrors = ValidateInputs(_inputText, _dimension == 1, out unused);
                    Repaint();
                }

                var style = new GUIStyle(EditorStyles.label);
                if (_inputErrors[index] != null)
                    style.normal.textColor = style.hover.textColor =
                        style.active.textColor = style.focused.textColor = new Color(1f, 0.3f, 0.3f);
                GUI.Label(labelRect, InputNames[index], style);
                GUILayout.Label(InputHint(index), EditorStyles.wordWrappedMiniLabel,
                    GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
                GUI.Label(row.rect, new GUIContent("", InputTooltip(index)), GUIStyle.none);
            }
            EditorGUILayout.Space(2);
        }

        private static string InputErrorSummary(string[] errors)
        {
            return string.Join("\n", errors.Select((e, i) => e == null ? null : InputNames[i] + ": " + e).Where(e => e != null));
        }

        private bool TryRequest(out TerraKitBackendGenerationRequest request, out string error)
        {
            EnsureInputText();
            _inputErrors = ValidateInputs(_inputText, _dimension == 1, out request);
            error = InputErrorSummary(_inputErrors);
            if (_graph == null) { request = null; error = "Select a TerraKit graph." + (error.Length == 0 ? "" : "\n" + error); }
            return request != null;
        }

        internal static string[] ValidateInputs(string[] text, bool volume, out TerraKitBackendGenerationRequest request)
        {
            request = null;
            var errors = new string[8];
            var culture = CultureInfo.InvariantCulture;
            ulong seed;
            if (!ulong.TryParse(text[0], NumberStyles.Integer, culture, out seed)) errors[0] = InputHint(0);
            int lod;
            bool validLod = int.TryParse(text[1], NumberStyles.Integer, culture, out lod) && lod >= 0 && lod <= 30;
            if (!validLod) errors[1] = "Enter an integer from 0 to 30.";
            var cells = new[] { 16, 16, 16 };
            var spacing = new[] { 1.0, 1.0, 1.0 };
            int axes = volume ? 3 : 2;
            for (int axis = 0; axis < axes; axis++)
            {
                int c = 2 + axis, s = 5 + axis;
                bool validCell = int.TryParse(text[c], NumberStyles.Integer, culture, out cells[axis]) && cells[axis] >= 1 && cells[axis] <= 4096;
                if (!validCell) errors[c] = "Enter an integer from 1 to 4096.";
                if (validCell && validLod && cells[axis] % (1L << lod) != 0)
                {
                    errors[c] = "With LOD " + lod + ", this size must be divisible by " + (1L << lod) + ".";
                    errors[1] = "LOD does not match the Cell dimensions. Every active size must be divisible by 2^LOD.";
                }
                bool validSpacing = double.TryParse(text[s], NumberStyles.Float, culture, out spacing[axis]) &&
                    !double.IsNaN(spacing[axis]) && !double.IsInfinity(spacing[axis]) && spacing[axis] > 0;
                if (!validSpacing) errors[s] = "Enter a positive finite number, e.g. 0.5 or 1. Use '.' for decimals.";
                if (validCell && validSpacing)
                {
                    double coverage = cells[axis] * spacing[axis];
                    if (coverage > float.MaxValue)
                        errors[s] = "Region size exceeds Unity's coordinate range. Reduce spacing or Cell size.";
                }
            }
            if (errors[1] == null && Enumerable.Range(2, axes).All(i => errors[i] == null))
            {
                long samples = 1;
                for (int axis = 0; axis < axes; axis++) samples *= cells[axis] / (1L << lod) + 1;
                if (samples > 2000000)
                {
                    string message = "More than 2 million layout points. Reduce Cell sizes or increase a compatible LOD.";
                    errors[1] = message;
                    for (int axis = 0; axis < axes; axis++) errors[2 + axis] = message;
                }
            }
            if (errors.All(e => e == null))
                request = new TerraKitBackendGenerationRequest(seed, 0, 0, (ushort)lod,
                    (uint)cells[0], (uint)cells[1], spacing[0], spacing[1], volume, 0, (uint)cells[2], spacing[2]);
            return errors;
        }

        private static bool TryRegionCount(string text, out int count)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out count) && count >= 1;
        }

        private void DrawRegionCount(string label, ref string text)
        {
            using (var row = new EditorGUILayout.HorizontalScope())
            {
                Rect rect = GUILayoutUtility.GetRect(130, 24, GUILayout.Width(130));
                text = EditorGUILayout.TextField(text ?? "",
                    GUILayout.Width(120), GUILayout.Height(24));
                int count;
                var style = new GUIStyle(EditorStyles.label);
                int width, height, depth = 1;
                bool validX = TryRegionCount(_regionsX, out width);
                bool validY = TryRegionCount(_regionsY, out height);
                bool validZ = _dimension == 0 || TryRegionCount(_regionsZ, out depth);
                bool countsValid = validX && validY && validZ;
                if (!TryRegionCount(text, out count) || count > MaxRegionCount ||
                    (countsValid && RegionCountError(width, height, depth) != null))
                    style.normal.textColor = style.hover.textColor =
                        style.active.textColor = style.focused.textColor = new Color(1f, 0.3f, 0.3f);
                GUI.Label(rect, label, style);
                GUILayout.Label("Integer: 1–" + MaxRegionCount, EditorStyles.wordWrappedMiniLabel,
                    GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
                GUI.Label(row.rect, new GUIContent("", "Number of regions to generate along this axis."), GUIStyle.none);
            }
            EditorGUILayout.Space(2);
        }

        internal static string RegionCountError(int width, int height, int depth)
        {
            if (width < 1 || height < 1 || depth < 1)
                return "Region counts must be positive integers.";
            // Divide before multiplying so even int.MaxValue inputs cannot overflow.
            if (width > MaxRegionCount / height / depth)
                return "Too many regions. Reduce the region counts. Maximum: " + MaxRegionCount + ".";
            return null;
        }

        internal static void ValidateRegionLayout(
            TerraKitBackendGenerationRequest layout, int width, int height, int depth = 1)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (!layout.Is3D) depth = 1;
            string countError = RegionCountError(width, height, depth);
            if (countError != null) throw new ArgumentException(countError);
            if (layout.LodLevel > 30) throw new ArgumentOutOfRangeException(nameof(layout), "LOD must be from 0 to 30.");
            long points = checked(((long)layout.CellWidth / (1L << layout.LodLevel) + 1) *
                                 ((long)layout.CellHeight / (1L << layout.LodLevel) + 1));
            if (layout.Is3D) points = checked(points * ((long)layout.CellDepth / (1L << layout.LodLevel) + 1));
            if ((long)width * height > 4000000 / points / depth)
                throw new InvalidOperationException("Over 4 million layout points. Reduce region counts or resolution.");
            if (width * (layout.CellWidth * layout.SpacingX) > float.MaxValue ||
                height * (layout.CellHeight * layout.SpacingY) > float.MaxValue ||
                (layout.Is3D && depth * (layout.CellDepth * layout.SpacingZ) > float.MaxValue))
                throw new InvalidOperationException("Map size exceeds Unity's coordinate range. Reduce region counts or spacing.");
        }

        internal static List<TerraKitBackendGenerationRequest> BuildRegionRequests(
            TerraKitBackendGenerationRequest layout, int width, int height, int depth = 1)
        {
            ValidateRegionLayout(layout, width, height, depth);
            if (!layout.Is3D) depth = 1;
            var requests = new List<TerraKitBackendGenerationRequest>(width * height * depth);
            for (int z = 0; z < depth; z++)
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        // Region addresses only arrange this map around the Unity origin.
                        long rx = x - width / 2;
                        long ry = y - height / 2;
                        long rz = layout.Is3D ? z - depth / 2 : 0;
                        requests.Add(new TerraKitBackendGenerationRequest(layout.Seed, rx, ry, layout.LodLevel,
                            layout.CellWidth, layout.CellHeight, layout.SpacingX, layout.SpacingY,
                            layout.Is3D, rz, layout.CellDepth, layout.SpacingZ));
                    }
            return requests;
        }

        private void Generate(TerraKitBackendGenerationRequest layout, int width, int height, int depth)
        {
            var nextScene = new TerraKitOutputScenePreview();
            Texture2D nextPreview = null;
            _isGenerating = true;
            try
            {
                if (!layout.Is3D) depth = 1;
                var requests = BuildRegionRequests(layout, width, height, depth);
                string graphName = _graph.name;
                string snapshot = EditorJsonUtility.ToJson(_graph);
                var results = new TerraKitBackendGraphRunner().GenerateResources(_graph, requests,
                    (completed, total) => EditorUtility.DisplayCancelableProgressBar(
                        "TerraKit", "Generating regions: " + completed + " / " + total +
                        " completed. Cancellation takes effect after the current region finishes.",
                        (float)completed / total));
                int regionIndex = ((depth / 2) * height + height / 2) * width + width / 2;
                var resources = results[regionIndex].Resources;
                int outputIndex = 0;
                for (int i = 0; i < resources.Count; i++)
                    if (resources[i].Kind == TerraKitBackendResourceKind.Mesh) { outputIndex = i; break; }
                string generatedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                float minimum = 0, maximum = 0;
                var resource = resources.Count == 0 ? null : resources[outputIndex];
                if (EditorUtility.DisplayCancelableProgressBar("TerraKit", "Preparing map preview...", 1f))
                    throw new OperationCanceledException("Map generation cancelled.");
                if (resource?.Grid != null)
                {
                    TerraKitResourceExport.Range(resource.Grid, out minimum, out maximum);
                    nextPreview = TerraKitResourceExport.Preview(resource.Grid, 0, minimum, maximum);
                }
                if (results.Any(r => r.Resources.Any(o => o.Mesh != null)))
                    nextScene.Show(results, graphName);

                // Commit only after generation and preview creation have both succeeded.
                ClearResults();
                _scene = nextScene;
                nextScene = null;
                _preview = nextPreview;
                nextPreview = null;
                _results = results;
                _regionIndex = regionIndex;
                _outputIndex = outputIndex;
                _minimum = minimum;
                _maximum = maximum;
                _generatedGraphName = graphName;
                _snapshot = snapshot;
                _generatedAt = generatedAt;
                _message = "Generated " + results.Count + " region(s), " + resources.Count + " output(s) per region.";
                _messageType = MessageType.Info;
                if (_preview != null || _scene.HasVisibleMeshes)
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog("TerraKit", "Preview generated successfully.", "OK");
                }
            }
            catch (OperationCanceledException)
            {
                _message = _results == null ? "Generation cancelled. No results were committed."
                    : "Generation cancelled. Previous results and preview were kept.";
                _messageType = MessageType.Info;
            }
            catch (Exception exception)
            {
                ShowError(exception, _results == null ? "Generation failed."
                    : "Generation failed. Previous results and preview were kept.");
            }
            finally
            {
                nextScene?.Dispose();
                if (nextPreview != null) DestroyImmediate(nextPreview);
                _isGenerating = false;
                EditorUtility.ClearProgressBar();
                Repaint();
            }
        }

        private TerraKitGeneratedRegion CurrentRegion { get { return _results[_regionIndex]; } }
        private TerraKitGeneratedResource CurrentResource
        { get { return CurrentRegion.Resources.Count == 0 ? null : CurrentRegion.Resources[_outputIndex]; } }

        private void DrawGenerationSummary()
        {
            string timestampText = "";
            if (DateTimeOffset.TryParse(_generatedAt, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var generatedAt))
            {
                DateTimeOffset local = generatedAt.ToLocalTime();
                bool today = local.Date == DateTime.Today;
                timestampText = today ? "Generated at " : "Generated on ";
                timestampText += local.ToString(today ? "HH:mm:ss" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            else if (!string.IsNullOrEmpty(_generatedAt))
            {
                timestampText = _generatedAt;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var graphContent = new GUIContent(_generatedGraphName ?? "");
                float graphWidth = Mathf.Min(180, Mathf.Ceil(EditorStyles.miniLabel.CalcSize(graphContent).x));
                GUILayout.Label(graphContent, EditorStyles.miniLabel, GUILayout.Width(graphWidth));
                if (!string.IsNullOrEmpty(timestampText))
                {
                    GUILayout.Space(4);
                    // Keep the tooltip anchor limited to the timestamp text.
                    GUILayout.Label(new GUIContent(timestampText, "Original timestamp: " + _generatedAt),
                        EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                }
                GUILayout.FlexibleSpace();
            }
        }

        private string ShortNodeId(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return "";
            int length = Math.Min(8, nodeId.Length);
            while (length < nodeId.Length && CurrentRegion.Resources.Any(resource =>
                resource.Output.NodeId != nodeId &&
                (resource.Output.NodeId ?? "").StartsWith(nodeId.Substring(0, length), StringComparison.Ordinal)))
                length++;
            return nodeId.Substring(0, length);
        }

        private string OutputMenuLabel(TerraKitGeneratedResource resource)
        {
            var output = resource.Output;
            string nodeName = (output.NodeName ?? "Output").Replace('/', '-');
            string label = nodeName + " [" + ShortNodeId(output.NodeId) + "]";
            if (CurrentRegion.Resources.Count(item => item.Output.NodeId == output.NodeId && item.Kind == resource.Kind) > 1)
                label += " - " + (output.PortName ?? output.PortId ?? "Output").Replace('/', '-');
            return resource.Kind + "/" + label;
        }

        private int DrawOutputSelector()
        {
            Rect row = EditorGUILayout.GetControlRect();
            var labelStyle = new GUIStyle(EditorStyles.label);
            labelStyle.hover.textColor = labelStyle.active.textColor =
                labelStyle.focused.textColor = labelStyle.normal.textColor;
            GUI.Label(new Rect(row.x, row.y, EditorGUIUtility.labelWidth, row.height),
                "Output", labelStyle);
            row.xMin += EditorGUIUtility.labelWidth;
            return EditorGUI.Popup(row, _outputIndex,
                CurrentRegion.Resources.Select(OutputMenuLabel).ToArray());
        }

        private void DrawResults()
        {
            using (var panel = new EditorGUILayout.VerticalScope(ContentPanelStyle))
            {
                DrawContentPanelBackground(panel.rect);
                EditorGUILayout.LabelField("2  Results", EditorStyles.boldLabel);
                DrawGenerationSummary();
                if (_resultsStale)
                    EditorGUILayout.HelpBox("Showing the last successful generation. Current graph or settings have changed. Preview and exports still use the previous results until Generate Map succeeds.", MessageType.Warning);
                DrawParameterGroup("Selection");
                int region = EditorGUILayout.Popup("Region", _regionIndex,
                    _results.Select(r => TerraKitOutputScenePreview.RegionLabel(r.Request)).ToArray());
                if (region != _regionIndex)
                {
                    string address = CurrentResource?.Output.Address;
                    _regionIndex = region;
                    _outputIndex = Math.Max(0, CurrentRegion.Resources.ToList().FindIndex(r => r.Output.Address == address));
                    _slice = _sampleX = _sampleY = 0;
                    UpdatePreview();
                }
                var request = CurrentRegion.Request;
                if (CurrentRegion.Resources.Count == 0)
                { EditorGUILayout.HelpBox("The pipeline completed without declared resource outputs.", MessageType.Info); return; }
                int output = DrawOutputSelector();
                if (output != _outputIndex) { _outputIndex = output; _slice = _sampleX = _sampleY = 0; UpdatePreview(); }
                var resource = CurrentResource;
                if (resource.Mesh != null || resource.Grid != null)
                {
                    DrawParameterGroup("Output Info");
                    DrawOutputInfo(resource);
                }
                if (resource.Mesh != null)
                {
                    DrawParameterGroup("Scene Preview");
                    DrawSceneControls();
                }
                else if (resource.Grid != null)
                {
                    DrawParameterGroup("Data Preview");
                    DrawGrid(resource);
                }
                else
                    EditorGUILayout.HelpBox("No viewer is available for this output.", MessageType.Warning);

                if (resource.Mesh != null)
                {
                    DrawParameterGroup("Save");
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(!CurrentRegion.Resources.Any(item => item.Mesh != null)))
                            if (ActionButton("Save Mesh", inline: true,
                                tooltip: "Save all Mesh outputs in the selected Region, with their materials. Includes a Region Prefab to preserve material assignments. Other Regions are excluded."))
                                RunAction(() => _scene.SaveRegionMeshes(_results, _generatedGraphName, _regionIndex),
                                    "Selected Region saved with Meshes, materials and a Region Prefab.");
                        using (new EditorGUI.DisabledScope(!HasMapMeshes))
                            if (ActionButton("Save Prefab", inline: true,
                                tooltip: "Save all generated Regions and all Mesh outputs with their materials as one map Prefab, then place an instance at the Unity origin. Region and Output selection do not limit this save."))
                                RunAction(() => _scene.SavePrefab(_results, _generatedGraphName),
                                    "Complete map saved as a Prefab and placed at the Unity origin.");
                        GUILayout.FlexibleSpace();
                    }
                }

                DrawParameterGroup("Export");
                using (new EditorGUILayout.HorizontalScope())
                {
                    string jsonTooltip = resource.Kind == TerraKitBackendResourceKind.HeightField
                        ? "Export all original height samples for the selected region and output, with transforms and generation settings."
                        : resource.Kind == TerraKitBackendResourceKind.DensityField
                            ? "Export the complete 3D density data for the selected region and output, including every Z slice."
                            : resource.Kind == TerraKitBackendResourceKind.VoxelVolume
                                ? "Export the complete voxel data for the selected region and output, including every Z slice and original voxel ID."
                                : "Export the selected region and output: original data, transforms and generation settings.";
                    if (ActionButton("Export JSON", inline: true,
                        tooltip: jsonTooltip))
                        RunAction(() =>
                        {
                            string path = EditorUtility.SaveFilePanel("Export TerraKit Result", "", ExportName(resource), "json");
                            if (string.IsNullOrEmpty(path)) return false;
                            File.WriteAllText(path, TerraKitResourceExport.ToJson(resource, request, _generatedGraphName, _snapshot, _generatedAt));
                            _message = "Exported original data to " + path;
                            _messageType = MessageType.Info;
                            return true;
                        }, "JSON exported successfully.");
                    if (resource.Mesh != null)
                        using (new EditorGUI.DisabledScope(!HasMapMeshes))
                            if (ActionButton("Export PNG", inline: true,
                                tooltip: "Export the complete map as an automatically framed PNG. Preview is optional."))
                                RunAction(() =>
                                {
                                    string path = _scene.SaveImage(_results, _generatedGraphName);
                                    if (string.IsNullOrEmpty(path)) return false;
                                    _message = "Exported Mesh preview image to " + path;
                                    _messageType = MessageType.Info;
                                    return true;
                                }, "PNG image exported successfully.");
                    if (resource.Grid != null)
                    {
                        bool isHeightmap = resource.Kind == TerraKitBackendResourceKind.HeightField;
                        string imageLabel = isHeightmap ? "Heightmap" : "Slice Image";
                        string imageTooltip = isHeightmap
                            ? "Export a grayscale heightmap PNG at the original grid resolution."
                            : resource.Kind == TerraKitBackendResourceKind.DensityField
                                ? "Export only the current Z slice (XY plane) as a grayscale PNG at the original grid resolution."
                                : "Export only the current Z slice (XY plane) as a voxel-color PNG at the original grid resolution.";
                        if (ActionButton("Export " + imageLabel, inline: true,
                            tooltip: imageTooltip))
                            RunAction(() => ExportGridImage(resource, imageLabel),
                                imageLabel + " exported as PNG successfully.");
                    }
                    GUILayout.FlexibleSpace();
                }
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(resource.Mesh != null
                    ? "Save Mesh: selected Region, including all its Mesh outputs and materials. Save Prefab: all Regions with materials. Preview and PNG: complete map. JSON: selected result."
                    : resource.Kind == TerraKitBackendResourceKind.HeightField
                        ? "JSON: original height data. Heightmap: grayscale PNG image."
                        : resource.Kind == TerraKitBackendResourceKind.DensityField
                            ? "JSON: complete 3D density data. Slice Image: current Z slice as PNG."
                            : "JSON: complete voxel data. Slice Image: current Z slice as PNG.",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private bool ExportGridImage(TerraKitGeneratedResource resource, string imageLabel)
        {
            bool isVolume = resource.Kind != TerraKitBackendResourceKind.HeightField;
            string imageName = isVolume ? resource.Kind + "_Z" + _slice : "Heightmap";
            string name = ExportName(resource) + "_" + imageName;
            string path = EditorUtility.SaveFilePanel("Export " + imageLabel, "", name, "png");
            if (string.IsNullOrEmpty(path)) return false;

            Texture2D image = null;
            try
            {
                // Export only the data image, without axes, markers or editor UI.
                image = TerraKitResourceExport.GridImage(resource.Grid, _slice, _minimum, _maximum);
                byte[] png = image.EncodeToPNG();
                if (png == null || png.Length == 0)
                    throw new InvalidOperationException("Could not encode the grid image as PNG.");
                File.WriteAllBytes(path, png);
                _message = "Exported " + imageLabel + " to " + path;
                _messageType = MessageType.Info;
                return true;
            }
            finally { if (image != null) DestroyImmediate(image); }
        }

        private static void DrawResultNote(string text, string tooltip)
        {
            EditorGUILayout.LabelField(new GUIContent(text, tooltip), EditorStyles.wordWrappedMiniLabel);
        }

        private string ExportName(TerraKitGeneratedResource resource)
        {
            var request = CurrentRegion.Request;
            string name = _generatedGraphName + "_output" + resource.Output.ResourceKey + "_" +
                          request.RegionX + "_" + request.RegionY + "_" + request.RegionZ;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private bool HasMapMeshes => _results != null &&
            _results.Any(region => region.Resources.Any(resource => resource.Mesh != null));

        private void DrawSceneControls()
        {
            using (new EditorGUI.DisabledScope(!HasMapMeshes))
                if (ActionButton("Show Map Preview"))
                    RunAction(() =>
                    {
                        _scene.Show(_results, _generatedGraphName);
                        return _scene.HasVisibleMeshes;
                    }, "Complete map preview shown successfully.");
            EditorGUILayout.LabelField("Preview and Prefab use the complete map layout around the Unity origin.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(6);
        }

        private void DrawOutputInfo(TerraKitGeneratedResource resource)
        {
            if (resource.Mesh != null)
            {
                var mesh = resource.Mesh;
                DrawIdentifier("Vertices", mesh.Positions.Length.ToString(CultureInfo.InvariantCulture));
                DrawIdentifier("Triangles", (mesh.Indices.Length / 3).ToString(CultureInfo.InvariantCulture));
                DrawIdentifier("World Origin", string.Format(CultureInfo.InvariantCulture,
                    "{0}, {1}, {2}", mesh.WorldOrigin.X, mesh.WorldOrigin.Y, mesh.WorldOrigin.Z));
            }
            else if (resource.Grid != null)
            {
                if (_preview == null) UpdatePreview();
                var grid = resource.Grid;
                DrawIdentifier("Samples", grid.Width + " × " + grid.Height + " × " + grid.Depth);
                DrawIdentifier("Sampling", grid.Sampling == 1 ? "Point sampled" : "Cell sampled");
                if (grid.Values != null)
                    DrawIdentifier("Range", _minimum.ToString("G9", CultureInfo.InvariantCulture) +
                        " to " + _maximum.ToString("G9", CultureInfo.InvariantCulture));
            }
        }

        private void DrawGrid(TerraKitGeneratedResource resource)
        {
            var grid = resource.Grid;
            if (grid.Depth > 1)
            {
                int slice = EditorGUILayout.IntSlider("Z slice (XY plane)", _slice, 0, grid.Depth - 1);
                if (slice != _slice) { _slice = slice; UpdatePreview(false); }
            }
            if (_preview == null) UpdatePreview();
            _sampleX = EditorGUILayout.IntSlider("Sample X", _sampleX, 0, grid.Width - 1);
            _sampleY = EditorGUILayout.IntSlider("Sample Y", _sampleY, 0, grid.Height - 1);
            int index = _sampleX + grid.Width * (_sampleY + grid.Height * _slice);
            string value = grid.VoxelIds != null
                ? grid.VoxelIds[index].ToString(CultureInfo.InvariantCulture)
                : grid.Values[index].ToString("R", CultureInfo.InvariantCulture);
            EditorGUILayout.LabelField(grid.VoxelIds != null ? "Raw voxel ID" : "Raw sample value", value);
            if (_preview != null) DrawSamplePreview(grid, value);
            DrawResultNote(grid.VoxelIds != null ? "Colors represent voxel IDs." : "Grayscale represents sample values.",
                grid.VoxelIds != null
                    ? "Colors distinguish raw voxel IDs, not biome or material names."
                    : "Grayscale uses the full field range. Constant fields are mid-gray. Magenta marks non-finite values.");
            if (_preview != null && (grid.Width > _preview.width || grid.Height > _preview.height))
                EditorGUILayout.LabelField("Reduced image resolution. Marker value uses the original sample.",
                    EditorStyles.wordWrappedMiniLabel);
            if (resource.Kind != TerraKitBackendResourceKind.HeightField)
                DrawResultNote("Volume preview: XY slices.",
                    "To display a surface Mesh, connect a compatible backend meshing stage when one is available.");
        }

        private void DrawSamplePreview(TerraKitGridData grid, string value)
        {
            float desiredHeight = Mathf.Clamp(420f * _preview.height / _preview.width, 80, 320);
            Rect area = GUILayoutUtility.GetRect(0, desiredHeight + 48, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;

            // Fit the texture first so overlays follow its actual size, including letterboxing.
            float availableWidth = Mathf.Max(1, Mathf.Min(420, area.width - 48));
            float scale = Mathf.Min(availableWidth / _preview.width, desiredHeight / _preview.height);
            float width = _preview.width * scale, height = _preview.height * scale;
            Rect plot = new Rect(area.center.x - width * 0.5f + 10,
                area.y + 18 + (desiredHeight - height) * 0.5f, width, height);
            GUI.DrawTexture(plot, _preview, ScaleMode.StretchToFill, false);

            var axisStyle = new GUIStyle(EditorStyles.miniLabel);
            axisStyle.normal.textColor = EditorGUIUtility.isProSkin
                ? new Color(0.8f, 0.82f, 0.85f) : new Color(0.2f, 0.22f, 0.25f);
            Vector2 origin = new Vector2(plot.x - 10, plot.yMax + 10);
            Color previousColor = Handles.color;
            Handles.BeginGUI();
            try
            {
                Handles.color = axisStyle.normal.textColor;
                DrawPreviewArrow(origin, new Vector2(plot.xMax, origin.y));
                DrawPreviewArrow(origin, new Vector2(origin.x, plot.y));
            }
            finally { Handles.color = previousColor; Handles.EndGUI(); }
            GUI.Label(new Rect(plot.xMax + 4, origin.y - 8, 18, 18), "X", axisStyle);
            GUI.Label(new Rect(origin.x - 5, plot.y - 18, 18, 18), "Y", axisStyle);
            GUI.Label(new Rect(origin.x - 10, origin.y + 2, 44, 18), "(0, 0)", axisStyle);

            float x = plot.x + SamplePreviewCoordinate(_sampleX, grid.Width, _preview.width) * plot.width;
            float y = plot.yMax - SamplePreviewCoordinate(_sampleY, grid.Height, _preview.height) * plot.height;
            // Clip the outlined cross to the image, including corner samples.
            GUI.BeginClip(plot);
            try
            {
                Vector2 point = new Vector2(x - plot.x, y - plot.y);
                EditorGUI.DrawRect(new Rect(point.x - 8, point.y - 2, 16, 4), Color.black);
                EditorGUI.DrawRect(new Rect(point.x - 2, point.y - 8, 4, 16), Color.black);
                EditorGUI.DrawRect(new Rect(point.x - 7, point.y - 0.5f, 14, 1), Color.white);
                EditorGUI.DrawRect(new Rect(point.x - 0.5f, point.y - 7, 1, 14), Color.white);
            }
            finally { GUI.EndClip(); }

            string coordinates = grid.Depth > 1
                ? "(" + _sampleX + ", " + _sampleY + ", " + _slice + ")"
                : "(" + _sampleX + ", " + _sampleY + ")";
            var content = new GUIContent(coordinates + ": " + value);
            var badgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                padding = new RectOffset(6, 6, 4, 4)
            };
            badgeStyle.normal.textColor = Color.white;
            // Thin images need the surrounding chart area to fit a readable value label.
            Rect bounds = plot.width >= 140 && plot.height >= 60 ? plot : area;
            float badgeWidth = Mathf.Min(badgeStyle.CalcSize(content).x, Mathf.Max(1, bounds.width - 8));
            float badgeHeight = badgeStyle.CalcHeight(content, badgeWidth);
            float labelX = x + 12;
            if (labelX + badgeWidth > bounds.xMax - 4) labelX = x - 12 - badgeWidth;
            float labelY = y - badgeHeight - 10;
            if (labelY < bounds.y + 4) labelY = y + 10;
            Rect badge = new Rect(
                Mathf.Clamp(labelX, bounds.x + 4, Mathf.Max(bounds.x + 4, bounds.xMax - badgeWidth - 4)),
                Mathf.Clamp(labelY, bounds.y + 4, Mathf.Max(bounds.y + 4, bounds.yMax - badgeHeight - 4)),
                badgeWidth, badgeHeight);
            EditorGUI.DrawRect(badge, new Color(0.06f, 0.07f, 0.08f, 0.94f));
            GUI.Label(badge, content, badgeStyle);
        }

        private static void DrawPreviewArrow(Vector2 start, Vector2 end)
        {
            Vector2 direction = (end - start).normalized;
            Vector2 side = new Vector2(-direction.y, direction.x);
            Vector2 tail = end - direction * 5;
            Handles.DrawAAPolyLine(2, new Vector3(start.x, start.y), new Vector3(end.x, end.y));
            Handles.DrawAAPolyLine(2, new Vector3(tail.x + side.x * 3, tail.y + side.y * 3),
                new Vector3(end.x, end.y), new Vector3(tail.x - side.x * 3, tail.y - side.y * 3));
        }

        private static float SamplePreviewCoordinate(int sample, int sampleCount, int pixelCount)
        {
            if (sampleCount <= 1 || pixelCount <= 1) return 0.5f;
            sample = Mathf.Clamp(sample, 0, sampleCount - 1);
            // Match the integer resampling used by TerraKitResourceExport.Preview.
            int low = 0, high = pixelCount - 1;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                int source = (int)((long)middle * (sampleCount - 1) / (pixelCount - 1));
                if (source <= sample) low = middle;
                else high = middle - 1;
            }
            float pixel = low;
            if (low < pixelCount - 1)
            {
                int first = (int)((long)low * (sampleCount - 1) / (pixelCount - 1));
                int next = (int)((long)(low + 1) * (sampleCount - 1) / (pixelCount - 1));
                pixel += (float)(sample - first) / (next - first);
            }
            return (pixel + 0.5f) / pixelCount;
        }

        private void UpdatePreview(bool recalculateRange = true)
        {
            ClearTexture();
            var resource = CurrentResource;
            if (resource == null || resource.Grid == null) return;
            _slice = Math.Min(_slice, resource.Grid.Depth - 1);
            if (recalculateRange) TerraKitResourceExport.Range(resource.Grid, out _minimum, out _maximum);
            _preview = TerraKitResourceExport.Preview(resource.Grid, _slice, _minimum, _maximum);
        }

        private void RunAction(Func<bool> action, string successMessage)
        {
            RunAction(action, () => successMessage);
        }

        private void RunAction(Func<bool> action, Func<string> successMessage)
        {
            try
            {
                // Cancelled operations return false; exceptions use the existing error path.
                if (action()) EditorUtility.DisplayDialog("TerraKit", successMessage(), "OK");
            }
            catch (Exception exception) { ShowError(exception, "Action failed."); }
        }

        private void ShowError(Exception exception, string summary)
        {
            _message = summary + " See Graph info.";
            _messageType = MessageType.Error;
            TerraKitGraphWindow.OpenForGraph(_graph, error: exception.Message,
                result: (exception as TerraKitGraphValidationException)?.Result);
            Debug.LogException(exception);
        }
    }
}
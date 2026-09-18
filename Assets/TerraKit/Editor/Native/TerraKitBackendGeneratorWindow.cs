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
        [SerializeField] private TerraKitGraphAsset _graph;
        [SerializeField] private string _seed = "12345";
        [SerializeField] private string _regionsX = "3", _regionsY = "3", _regionsZ = "3";
        [SerializeField] private long _regionX, _regionY, _regionZ;
        [SerializeField] private int _lodLevel, _dimension;
        [SerializeField] private int _cellWidth = 16, _cellHeight = 16, _cellDepth = 16;
        [SerializeField] private double _spacingX = 1, _spacingY = 1, _spacingZ = 1;
        private Vector2 _scroll;
        private IReadOnlyList<TerraKitGeneratedRegion> _results;
        private int _regionIndex, _outputIndex, _slice, _sampleX, _sampleY;
        private string _message = "Select a graph, then Generate.";
        private MessageType _messageType = MessageType.Info;
        private string _generatedGraphName, _snapshot, _generatedAt;
        private TerraKitGraphAsset _observedGraph;
        private string _observedContent, _observedRequest;
        private Texture2D _preview;
        private float _minimum, _maximum;
        private readonly TerraKitOutputScenePreview _scene = new TerraKitOutputScenePreview();
        [SerializeField] private string[] _inputText;
        private string[] _inputErrors = new string[11];
        private static readonly string[] InputNames = { "Seed", "Region X", "Region Y", "Region Z", "LOD",
            "Cell Width", "Cell Height", "Cell Depth", "Base Spacing X", "Base Spacing Y", "Base Spacing Z" };

        private void ObserveChanges()
        {
            EnsureInputText();
            string content = _graph == null ? null : EditorJsonUtility.ToJson(_graph);
            string request = _dimension + ":" + string.Join("\n", _inputText) + ":" + _regionsX + ":" + _regionsY + (_dimension == 1 ? ":" + _regionsZ : "");
            bool graphChanged = _graph != _observedGraph || content != _observedContent;
            if (!graphChanged && request == _observedRequest) return;
            bool first = _observedRequest == null || _graph != _observedGraph;
            _observedGraph = _graph;
            _observedContent = content;
            _observedRequest = request;
            ClearResults();
            _message = _graph == null ? "Select a graph."
                : first ? "Click Generate All Outputs."
                : graphChanged ? "Graph changed. Generate again."
                : "Settings changed. Generate again.";
            _messageType = MessageType.Info;
        }

        private void ClearResults()
        {
            ClearTexture();
            _scene.Dispose();
            _results = null;
            _generatedGraphName = _snapshot = _generatedAt = null;
            _regionIndex = _outputIndex = _slice = _sampleX = _sampleY = 0;
        }

        internal static void OpenForGraph(TerraKitGraphAsset graph)
        {
            var window = GetWindow<TerraKitBackendGeneratorWindow>("Backend Generator");
            window.minSize = new Vector2(480, 650);
            if (graph != null) window._graph = graph;
            window.Show();
        }

        private void OnDisable() { ClearResults(); }
        private void OnInspectorUpdate() { ObserveChanges(); Repaint(); }
        private void ClearTexture() { if (_preview != null) DestroyImmediate(_preview); _preview = null; }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("TerraKit Backend Generator", EditorStyles.largeLabel);
            EditorGUILayout.LabelField("Run a pipeline. Inspect any output. Export the original data.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(8);
            DrawRequest();
            ObserveChanges();
            string validation;
            TerraKitBackendGenerationRequest request;
            bool valid = TryRequest(out request, out validation);
            if (!valid) EditorGUILayout.HelpBox(validation, MessageType.Warning);
            int width, height, depth = 1;
            bool validX = TryRegionCount(_regionsX, out width), validY = TryRegionCount(_regionsY, out height);
            bool validZ = _dimension == 0 || TryRegionCount(_regionsZ, out depth);
            bool validCounts = validX && validY && validZ;
            if (!validCounts)
                EditorGUILayout.HelpBox(string.Join("\n", new[] {
                    validX ? null : "Regions X: Enter an integer from 1 to 2147483647.",
                    validY ? null : "Regions Y: Enter an integer from 1 to 2147483647.",
                    validZ ? null : "Regions Z: Enter an integer from 1 to 2147483647."
                }.Where(e => e != null)), MessageType.Warning);
            using (new EditorGUI.DisabledScope(!valid || !validCounts))
                if (GUILayout.Button("Generate All Outputs", GUILayout.Height(34))) Generate(request, width, height, depth);
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(_message, _messageType);
            if (_results != null) DrawResults();
            EditorGUILayout.Space(10);
            EditorGUILayout.EndScrollView();
        }

        private void DrawRequest()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("1  Graph & request", EditorStyles.boldLabel);
                _graph = (TerraKitGraphAsset)EditorGUILayout.ObjectField("Graph", _graph, typeof(TerraKitGraphAsset), false);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!(Selection.activeObject is TerraKitGraphAsset)))
                        if (GUILayout.Button("Use Project Selection")) _graph = Selection.activeObject as TerraKitGraphAsset;
                    if (GUILayout.Button("Graph Editor")) TerraKitGraphWindow.OpenForGraph(_graph);
                }
                _dimension = EditorGUILayout.Popup("Region mode", _dimension, new[] { "2D — surface regions", "3D — volume regions" });
                EnsureInputText();
                TerraKitBackendGenerationRequest unused;
                _inputErrors = ValidateInputs(_inputText, _dimension == 1, out unused);
                for (int i = 0; i < InputNames.Length; i++)
                    if (_dimension == 1 || (i != 3 && i != 7 && i != 10)) DrawInput(i);
                DrawRegionCount("Regions X", ref _regionsX);
                DrawRegionCount(_dimension == 0 ? "Regions Y (world Z)" : "Regions Y", ref _regionsY);
                if (_dimension == 1) DrawRegionCount("Regions Z", ref _regionsZ);
                EditorGUILayout.LabelField("Centered on Region coordinates. Even counts extend one more region toward negative coordinates.", EditorStyles.wordWrappedMiniLabel);
                if (_dimension == 1)
                    EditorGUILayout.HelpBox("3D requires compatible stages. Built-in height stages support 2D only.", MessageType.Info);
                if (GUILayout.Button("Reset Request Settings"))
                {
                    _seed = "12345"; _regionX = _regionY = _regionZ = 0; _lodLevel = _dimension = 0;
                    _cellWidth = _cellHeight = _cellDepth = 16; _spacingX = _spacingY = _spacingZ = 1;
                    _inputText = null;
                    _regionsX = _regionsY = _regionsZ = "3";
                    GUI.FocusControl(null);
                }
            }
        }

        private void EnsureInputText()
        {
            if (_inputText != null && _inputText.Length == 11) return;
            _inputText = new[] { _seed, _regionX.ToString(CultureInfo.InvariantCulture),
                _regionY.ToString(CultureInfo.InvariantCulture), _regionZ.ToString(CultureInfo.InvariantCulture),
                _lodLevel.ToString(CultureInfo.InvariantCulture), _cellWidth.ToString(CultureInfo.InvariantCulture),
                _cellHeight.ToString(CultureInfo.InvariantCulture), _cellDepth.ToString(CultureInfo.InvariantCulture),
                _spacingX.ToString("R", CultureInfo.InvariantCulture), _spacingY.ToString("R", CultureInfo.InvariantCulture),
                _spacingZ.ToString("R", CultureInfo.InvariantCulture) };
        }

        private static string InputHint(int index)
        {
            if (index == 0) return "Integer: 0 to 18446744073709551615.";
            if (index <= 3) return "Integer: -9223372036854775808 to 9223372036854775807.";
            if (index == 4) return "Integer: 0–30; Cell sizes must be multiples of 2^LOD.";
            if (index <= 7) return "Integer: 1–4096; multiple of 2^LOD.";
            return "Finite number > 0, e.g. 0.5.";
        }

        private void DrawInput(int index)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect labelRect = GUILayoutUtility.GetRect(130, EditorGUIUtility.singleLineHeight, GUILayout.Width(130));
                EditorGUI.BeginChangeCheck();
                _inputText[index] = EditorGUILayout.TextField(_inputText[index] ?? "", GUILayout.Width(160));
                if (EditorGUI.EndChangeCheck())
                {
                    TerraKitBackendGenerationRequest unused;
                    _inputErrors = ValidateInputs(_inputText, _dimension == 1, out unused);
                    Repaint();
                }
                var style = new GUIStyle(EditorStyles.label);
                if (_inputErrors[index] != null)
                    style.normal.textColor = style.hover.textColor = style.active.textColor =
                        style.focused.textColor = new Color(1f, 0.3f, 0.3f);
                string name = index == 2 && _dimension == 0 ? "Region Y (world Z)" : InputNames[index];
                GUI.Label(labelRect, name, style);
                GUILayout.Label(InputHint(index), EditorStyles.wordWrappedMiniLabel, GUILayout.ExpandWidth(true));
            }
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
            var errors = new string[11];
            var culture = CultureInfo.InvariantCulture;
            ulong seed;
            if (!ulong.TryParse(text[0], NumberStyles.Integer, culture, out seed)) errors[0] = InputHint(0);
            int lod;
            bool validLod = int.TryParse(text[4], NumberStyles.Integer, culture, out lod) && lod >= 0 && lod <= 30;
            if (!validLod) errors[4] = "Enter an integer from 0 to 30.";
            var coordinates = new long[3];
            var cells = new[] { 16, 16, 16 };
            var spacing = new[] { 1.0, 1.0, 1.0 };
            int axes = volume ? 3 : 2;
            for (int axis = 0; axis < axes; axis++)
            {
                int r = 1 + axis, c = 5 + axis, s = 8 + axis;
                if (!long.TryParse(text[r], NumberStyles.Integer, culture, out coordinates[axis])) errors[r] = InputHint(r);
                bool validCell = int.TryParse(text[c], NumberStyles.Integer, culture, out cells[axis]) && cells[axis] >= 1 && cells[axis] <= 4096;
                if (!validCell) errors[c] = "Enter an integer from 1 to 4096.";
                if (validCell && validLod && cells[axis] % (1L << lod) != 0)
                {
                    errors[c] = "With LOD " + lod + ", this size must be divisible by " + (1L << lod) + ".";
                    errors[4] = "LOD does not match the Cell dimensions. Every active size must be divisible by 2^LOD.";
                }
                bool validSpacing = double.TryParse(text[s], NumberStyles.Float, culture, out spacing[axis]) &&
                    !double.IsNaN(spacing[axis]) && !double.IsInfinity(spacing[axis]) && spacing[axis] > 0;
                if (!validSpacing) errors[s] = "Enter a positive finite number, e.g. 0.5 or 1. Use '.' for decimals.";
                if (validCell && validSpacing)
                {
                    double coverage = cells[axis] * spacing[axis];
                    if (double.IsInfinity(coverage)) errors[s] = "Cell size multiplied by spacing is too large. Reduce this spacing or the Cell size.";
                    else if (errors[r] == null && double.IsInfinity(coordinates[axis] * coverage))
                    {
                        errors[r] = "World position overflows. Reduce this coordinate or its spacing.";
                        errors[s] = "World position overflows. Reduce this spacing or its Region coordinate.";
                    }
                }
            }
            if (errors[4] == null && Enumerable.Range(5, axes).All(i => errors[i] == null))
            {
                long samples = 1;
                for (int axis = 0; axis < axes; axis++) samples *= cells[axis] / (1L << lod) + 1;
                if (samples > 2000000)
                {
                    string message = "More than 2 million layout points. Reduce Cell sizes or increase a compatible LOD.";
                    errors[4] = message;
                    for (int axis = 0; axis < axes; axis++) errors[5 + axis] = message;
                }
            }
            if (errors.All(e => e == null))
                request = new TerraKitBackendGenerationRequest(seed, coordinates[0], coordinates[1], (ushort)lod,
                    (uint)cells[0], (uint)cells[1], spacing[0], spacing[1], volume, coordinates[2], (uint)cells[2], spacing[2]);
            return errors;
        }

        private static bool TryRegionCount(string text, out int count)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out count) && count >= 1;
        }

        private static void DrawRegionCount(string label, ref string text)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect rect = GUILayoutUtility.GetRect(130, EditorGUIUtility.singleLineHeight, GUILayout.Width(130));
                text = EditorGUILayout.TextField(text ?? "", GUILayout.Width(160));
                int count;
                var style = new GUIStyle(EditorStyles.label);
                if (!TryRegionCount(text, out count))
                    style.normal.textColor = style.hover.textColor = style.active.textColor =
                        style.focused.textColor = new Color(1f, 0.3f, 0.3f);
                GUI.Label(rect, label, style);
                GUILayout.Label("Integer: 1–2147483647.", EditorStyles.wordWrappedMiniLabel);
            }
        }

        internal static List<TerraKitBackendGenerationRequest> BuildRegionRequests(
            TerraKitBackendGenerationRequest center, int width, int height, int depth = 1)
        {
            if (!center.Is3D) depth = 1;
            if (width < 1 || height < 1 || depth < 1)
                throw new ArgumentOutOfRangeException("width", "Region counts must be positive integers.");
            if (width == 1 && height == 1 && depth == 1)
                return new List<TerraKitBackendGenerationRequest> { center };
            long points = ((long)center.CellWidth / (1L << center.LodLevel) + 1) *
                          ((long)center.CellHeight / (1L << center.LodLevel) + 1);
            if (center.Is3D) points = checked(points * ((long)center.CellDepth / (1L << center.LodLevel) + 1));
            // Divide the budget first so very large region counts cannot overflow.
            if ((long)width * height > 4000000 / points / depth)
                throw new InvalidOperationException("Over 4 million layout points. Reduce region counts or resolution.");
            var requests = new List<TerraKitBackendGenerationRequest>();
            for (int z = 0; z < depth; z++)
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        long rx = checked(center.RegionX + (x - width / 2));
                        long ry = checked(center.RegionY + (y - height / 2));
                        long rz = center.Is3D ? checked(center.RegionZ + (z - depth / 2)) : center.RegionZ;
                        if (double.IsInfinity(rx * (center.CellWidth * center.SpacingX)) ||
                            double.IsInfinity(ry * (center.CellHeight * center.SpacingY)) ||
                            (center.Is3D && double.IsInfinity(rz * (center.CellDepth * center.SpacingZ))))
                            throw new InvalidOperationException("World position is too large. Reduce coordinates or spacing.");
                        requests.Add(new TerraKitBackendGenerationRequest(center.Seed, rx, ry, center.LodLevel,
                            center.CellWidth, center.CellHeight, center.SpacingX, center.SpacingY,
                            center.Is3D, rz, center.CellDepth, center.SpacingZ));
                    }
            return requests;
        }

        private void Generate(TerraKitBackendGenerationRequest center, int width, int height, int depth)
        {
            try
            {
                ClearResults();
                if (!center.Is3D) depth = 1;
                var requests = BuildRegionRequests(center, width, height, depth);
                EditorUtility.DisplayProgressBar("TerraKit", "Generating and copying backend outputs…", 0.5f);
                var results = new TerraKitBackendGraphRunner().GenerateResources(_graph, requests);
                _results = results; _regionIndex = ((depth / 2) * height + height / 2) * width + width / 2; _outputIndex = 0;
                _slice = _sampleX = _sampleY = 0;
                _generatedGraphName = _graph.name; _snapshot = EditorJsonUtility.ToJson(_graph);
                _generatedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                var resources = CurrentRegion.Resources;
                for (int i = 0; i < resources.Count; i++)
                    if (resources[i].Kind == TerraKitBackendResourceKind.Mesh) { _outputIndex = i; break; }
                UpdatePreview();
                _message = "Generated " + results.Count + " region(s), " + resources.Count + " output(s) per region.";
                _messageType = MessageType.Info;
                if (_results.Any(r => r.Resources.Any(o => o.Mesh != null)))
                    _scene.Show(_results, _generatedGraphName, -1);

                if (_preview != null || _scene.HasVisibleMeshes)
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog(
                        "TerraKit",
                        "Preview generated successfully.",
                        "OK");
                }
            }
            catch (Exception exception)
            {
                ClearResults();
                ShowError(exception, "Generation failed.");
            }
            finally { EditorUtility.ClearProgressBar(); Repaint(); }
        }

        private TerraKitGeneratedRegion CurrentRegion { get { return _results[_regionIndex]; } }
        private TerraKitGeneratedResource CurrentResource
        { get { return CurrentRegion.Resources.Count == 0 ? null : CurrentRegion.Resources[_outputIndex]; } }

        private void DrawResults()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("2  Results", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(_generatedGraphName + " • " + _generatedAt, EditorStyles.wordWrappedMiniLabel);
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
                EditorGUILayout.LabelField((request.Is3D ? "3D" : "2D") + " • Seed " + request.Seed + " • LOD " + request.LodLevel +
                    " • Cells " + request.CellWidth + " × " + request.CellHeight + (request.Is3D ? " × " + request.CellDepth : ""), EditorStyles.wordWrappedMiniLabel);
                if (CurrentRegion.Resources.Count == 0)
                { EditorGUILayout.HelpBox("The pipeline completed without declared resource outputs.", MessageType.Info); return; }
                int output = EditorGUILayout.Popup("Output", _outputIndex,
                    CurrentRegion.Resources.Select(TerraKitOutputScenePreview.Label).ToArray());
                if (output != _outputIndex) { _outputIndex = output; _slice = _sampleX = _sampleY = 0; UpdatePreview(); }
                var resource = CurrentResource;
                EditorGUILayout.LabelField("Node ID (matches Graph Inspector)");
                EditorGUILayout.SelectableLabel(resource.Output.NodeId, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.LabelField("Port ID", resource.Output.PortId);
                DrawSceneControls();
                if (resource.Mesh != null) DrawMesh(resource);
                else if (resource.Grid != null) DrawGrid(resource);
                else EditorGUILayout.HelpBox("No viewer is available for this output.", MessageType.Warning);
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("3  Export selected result", EditorStyles.boldLabel);
                if (GUILayout.Button("Export Original Data + Request (.json)")) RunAction(() =>
                {
                    string path = EditorUtility.SaveFilePanel("Export TerraKit Result", "", ExportName(resource), "json");
                    if (string.IsNullOrEmpty(path)) return;
                    File.WriteAllText(path, TerraKitResourceExport.ToJson(resource, request, _generatedGraphName, _snapshot, _generatedAt));
                    _message = "Exported original data to " + path; _messageType = MessageType.Info;
                });
                if (resource.Mesh != null)
                {
                    if (GUILayout.Button("Save Selected Mesh Asset"))
                        RunAction(() => TerraKitResourceExport.SaveMesh(resource.Mesh, ExportName(resource)));
                }
                else if (_preview != null && GUILayout.Button("Export Current Preview (.png)")) RunAction(() =>
                {
                    string path = EditorUtility.SaveFilePanel("Export Preview Image", "", ExportName(resource) + "_slice" + _slice, "png");
                    if (!string.IsNullOrEmpty(path)) File.WriteAllBytes(path, _preview.EncodeToPNG());
                });
                EditorGUILayout.LabelField("JSON preserves original samples, transforms and request values. PNG is a display preview, not a lossless data export.", EditorStyles.wordWrappedMiniLabel);
            }
        }

        private string ExportName(TerraKitGeneratedResource resource)
        {
            var request = CurrentRegion.Request;
            string name = _generatedGraphName + "_output" + resource.Output.ResourceKey + "_" +
                          request.RegionX + "_" + request.RegionY + "_" + request.RegionZ;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private void DrawSceneControls()
        {
            using (new EditorGUI.DisabledScope(!CurrentRegion.Resources.Any(r => r.Mesh != null)))
                if (GUILayout.Button("Show All Mesh Outputs — Selected Region"))
                    RunAction(() => _scene.Show(_results, _generatedGraphName, _regionIndex));
            if (_results.Count > 1)
                using (new EditorGUI.DisabledScope(!_results.Any(r => r.Resources.Any(o => o.Mesh != null))))
                    if (GUILayout.Button("Show All Mesh Outputs — All Regions"))
                        RunAction(() => _scene.Show(_results, _generatedGraphName, -1));
            using (new EditorGUI.DisabledScope(!_scene.HasVisibleMeshes))
                if (GUILayout.Button("Save Visible Meshes + Prefab"))
                    RunAction(() => _scene.SaveVisiblePrefab(_generatedGraphName));
            EditorGUILayout.LabelField("Each Mesh has its own Hierarchy object. Outputs keep their backend positions and may overlap. Toggle objects in the Hierarchy to compare.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("Scene previews are temporary and cleared when this window closes or scripts reload. Save a prefab to keep them.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(6);
        }

        private void DrawMesh(TerraKitGeneratedResource resource)
        {
            var mesh = resource.Mesh;
            EditorGUILayout.LabelField(mesh.Positions.Length + " vertices • " + mesh.Indices.Length / 3 + " triangles");
            EditorGUILayout.LabelField("World origin: " + mesh.WorldOrigin.X + ", " + mesh.WorldOrigin.Y + ", " + mesh.WorldOrigin.Z, EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Show Selected Mesh in Scene")) RunAction(() =>
                _scene.Show(_results, _generatedGraphName, _regionIndex, resource.Output.Address));
            if (_results.Count > 1 && GUILayout.Button("Show This Output Across All Regions")) RunAction(() =>
                _scene.Show(_results, _generatedGraphName, -1, resource.Output.Address));
        }

        private void DrawGrid(TerraKitGeneratedResource resource)
        {
            var grid = resource.Grid;
            EditorGUILayout.LabelField(grid.Width + " × " + grid.Height + " × " + grid.Depth + " samples • " +
                (grid.Sampling == 1 ? "Point sampled" : "Cell sampled"));
            if (grid.Values != null) EditorGUILayout.LabelField("Range: " + _minimum.ToString("G9") + " to " + _maximum.ToString("G9"));
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
            if (_preview != null)
            {
                float width = Mathf.Max(64, Mathf.Min(position.width - 60, 420));
                Rect rect = GUILayoutUtility.GetRect(width, Mathf.Clamp(width * grid.Height / grid.Width, 80, 320));
                GUI.DrawTexture(rect, _preview, ScaleMode.ScaleToFit, false);
            }
            EditorGUILayout.LabelField(grid.VoxelIds != null
                ? "Colors distinguish raw voxel IDs; they do not imply biome or material names."
                : "Grayscale uses the full field range. Constant fields are mid-gray. Magenta marks non-finite values.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("Grid X increases right; grid Y increases upward. Preview is capped at 512 samples per axis.", EditorStyles.wordWrappedMiniLabel);
            if (resource.Kind != TerraKitBackendResourceKind.HeightField)
                EditorGUILayout.HelpBox("Volume outputs are inspected as XY slices. To display a surface Mesh, connect a compatible backend meshing stage when one is available.", MessageType.Info);
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

        private void RunAction(Action action)
        {
            try { action(); }
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
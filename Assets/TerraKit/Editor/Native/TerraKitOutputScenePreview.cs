using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TerraKit.Editor
{
    internal sealed class TerraKitOutputScenePreview : IDisposable
    {
        private sealed class Entry
        {
            internal int Region;
            internal string Address;
            internal GameObject Object;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private GameObject _root;
        private IReadOnlyList<TerraKitGeneratedRegion> _results;

        internal static string Label(TerraKitGeneratedResource resource)
        {
            var output = resource.Output;
            string id = output.NodeId ?? "";
            if (id.Length > 8) id = id.Substring(0, 8);
            return "#" + output.ResourceKey + " " + output.NodeName + " [" + id + "] / " +
                   output.PortName + " (" + output.Kind + ")";
        }

        internal static string RegionLabel(TerraKitBackendGenerationRequest request)
        {
            return "(" + request.RegionX + ", " + request.RegionY +
                   (request.Is3D ? ", " + request.RegionZ : "") + ")";
        }

        internal bool HasVisibleMeshes
        {
            get { return _entries.Any(e => e.Object != null && e.Object.activeInHierarchy); }
        }

        // regionIndex = -1: all generated regions. address = null: all Mesh outputs.
        internal void Show(IReadOnlyList<TerraKitGeneratedRegion> results, string graphName,
            int regionIndex, string address = null)
        {
            if (_root == null || !ReferenceEquals(_results, results)) Build(results, graphName);
            _root.SetActive(true);
            foreach (var entry in _entries)
            {
                if (entry.Object == null) continue;
                entry.Object.transform.parent.gameObject.SetActive(true);
                entry.Object.SetActive((regionIndex < 0 || entry.Region == regionIndex) &&
                                       (address == null || entry.Address == address));
            }
            var visible = _entries.Where(e => e.Object != null && e.Object.activeInHierarchy).ToArray();
            if (visible.Length == 0) return;
            Bounds bounds = visible[0].Object.GetComponent<Renderer>().bounds;
            foreach (var entry in visible) bounds.Encapsulate(entry.Object.GetComponent<Renderer>().bounds);
            Selection.activeGameObject = visible.Length == 1 ? visible[0].Object : _root;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(bounds, false);
            SceneView.RepaintAll();
        }

        private void Build(IReadOnlyList<TerraKitGeneratedRegion> results, string graphName)
        {
            Dispose();
            try
            {
                _root = NewObject("TerraKit Outputs — " + graphName, null);
                var pipeline = GraphicsSettings.currentRenderPipeline;
                if (pipeline == null) pipeline = GraphicsSettings.defaultRenderPipeline;
                Material material = pipeline != null ? pipeline.defaultMaterial : null;
                if (material == null) material = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
                for (int r = 0; r < results.Count; r++)
                {
                    GameObject region = null;
                    foreach (var resource in results[r].Resources)
                    {
                        if (resource.Mesh == null) continue;
                        if (region == null) region = NewObject("Region " + RegionLabel(results[r].Request), _root.transform);
                        var child = NewObject(Label(resource), region.transform);
                        var mesh = TerraKitResourceExport.CreateMesh(resource.Mesh);
                        _meshes.Add(mesh);
                        mesh.name = Label(resource);
                        mesh.hideFlags = HideFlags.HideAndDontSave;
                        child.AddComponent<MeshFilter>().sharedMesh = mesh;
                        child.AddComponent<MeshRenderer>().sharedMaterial = material;
                        child.transform.position = resource.Mesh.Origin;
                        _entries.Add(new Entry { Region = r, Address = resource.Output.Address, Object = child });
                    }
                }
                _results = results;
            }
            catch { Dispose(); throw; }
        }

        private static GameObject NewObject(string name, Transform parent)
        {
            var result = new GameObject(name);
            result.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            result.transform.SetParent(parent, false);
            return result;
        }

        internal void SaveVisiblePrefab(string graphName)
        {
            var visible = _entries.Where(e => e.Object != null && e.Object.activeInHierarchy).ToArray();
            if (visible.Length == 0) throw new InvalidOperationException("Show at least one Mesh first.");
            string path = EditorUtility.SaveFilePanelInProject("Save Visible Meshes + Prefab",
                graphName + "_Outputs", "prefab", "Save the currently visible Mesh outputs and their positions.");
            if (string.IsNullOrEmpty(path)) return;
            // Keep each export separate, including its mesh assets.
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string folder = AssetDatabase.GenerateUniqueAssetPath(parent + "/" + Path.GetFileNameWithoutExtension(path) + "_Meshes");
            var savedPaths = new List<string>();
            GameObject export = null;
            try
            {
                string guid = AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
                if (string.IsNullOrEmpty(guid)) throw new IOException("Could not create the Mesh asset folder.");
                savedPaths.Add(folder);
                export = new GameObject(Path.GetFileNameWithoutExtension(path));
                export.hideFlags = HideFlags.HideAndDontSave;
                var regions = new Dictionary<int, Transform>();
                for (int i = 0; i < visible.Length; i++)
                {
                    var entry = visible[i];
                    Transform region;
                    if (!regions.TryGetValue(entry.Region, out region))
                    {
                        region = new GameObject(entry.Object.transform.parent.name).transform;
                        region.SetParent(export.transform, false);
                        regions.Add(entry.Region, region);
                    }
                    var child = new GameObject(entry.Object.name);
                    child.transform.SetParent(region, false);
                    child.transform.position = entry.Object.transform.position;
                    child.transform.rotation = entry.Object.transform.rotation;
                    child.transform.localScale = entry.Object.transform.lossyScale;
                    var mesh = Object.Instantiate(entry.Object.GetComponent<MeshFilter>().sharedMesh);
                    mesh.hideFlags = HideFlags.None;
                    try { AssetDatabase.CreateAsset(mesh, folder + "/Mesh_" + (i + 1) + ".asset"); }
                    catch { Object.DestroyImmediate(mesh); throw; }
                    child.AddComponent<MeshFilter>().sharedMesh = mesh;
                    child.AddComponent<MeshRenderer>().sharedMaterial = entry.Object.GetComponent<MeshRenderer>().sharedMaterial;
                }
                // Prefab contents must be ordinary, serializable objects.
                export.hideFlags = HideFlags.None;
                savedPaths.Add(path);
                var prefab = PrefabUtility.SaveAsPrefabAsset(export, path);
                if (prefab == null) throw new IOException("Could not save the prefab.");
                AssetDatabase.SaveAssets();
                EditorGUIUtility.PingObject(prefab);
            }
            catch
            {
                for (int i = savedPaths.Count - 1; i >= 0; i--) AssetDatabase.DeleteAsset(savedPaths[i]);
                throw;
            }
            finally { if (export != null) Object.DestroyImmediate(export); }
        }

        public void Dispose()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            foreach (var mesh in _meshes) if (mesh != null) Object.DestroyImmediate(mesh);
            _entries.Clear(); _meshes.Clear(); _root = null; _results = null;
        }
    }
}
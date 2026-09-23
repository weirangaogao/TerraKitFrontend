using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TerraKit.Editor
{
    internal sealed class TerraKitOutputScenePreview : IDisposable
    {
        private readonly Dictionary<GameObject, int> _objectRegions = new Dictionary<GameObject, int>();
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private GameObject _root;
        private Scene _exportScene;
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
            get { return _objects.Any(item => item != null && item.activeInHierarchy); }
        }

        internal void Show(IReadOnlyList<TerraKitGeneratedRegion> results, string graphName)
        {
            if (_root == null || !ReferenceEquals(_results, results) || _objects.Any(item => item == null))
                Build(results, graphName);
            ShowAll();
            if (_objects.Count == 0) return;
            Bounds bounds = _objects[0].GetComponent<Renderer>().bounds;
            foreach (var item in _objects) bounds.Encapsulate(item.GetComponent<Renderer>().bounds);
            Selection.activeGameObject = _root;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(bounds, false);
            SceneView.RepaintAll();
        }

        private void ShowAll()
        {
            _root.SetActive(true);
            foreach (var item in _objects)
            {
                item.transform.parent.gameObject.SetActive(true);
                item.SetActive(true);
            }
        }

        private void Build(IReadOnlyList<TerraKitGeneratedRegion> results, string graphName)
        {
            Dispose();
            try
            {
                _root = NewObject("Preview-" + graphName, null);
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
                        // The Generator arranges regions around zero, so no origin rebasing is needed.
                        child.transform.localPosition = resource.Mesh.Origin;
                        _objects.Add(child);
                        _objectRegions.Add(child, r);
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

        // Export from a private snapshot without changing the live preview.
        private TerraKitOutputScenePreview PrepareExport(IReadOnlyList<TerraKitGeneratedRegion> results,
            string graphName)
        {
            var snapshot = new TerraKitOutputScenePreview();
            try
            {
                if (_root != null && ReferenceEquals(_results, results) &&
                    _objects.All(item => item != null && item.transform.IsChildOf(_root.transform)))
                {
                    snapshot._root = Object.Instantiate(_root);
                    snapshot._root.name = _root.name;
                    snapshot._results = results;
                    foreach (var item in _objects)
                    {
                        var indices = new Stack<int>();
                        var original = item.transform;
                        while (original != _root.transform)
                        {
                            indices.Push(original.GetSiblingIndex());
                            original = original.parent;
                        }
                        var copy = snapshot._root.transform;
                        while (indices.Count > 0) copy = copy.GetChild(indices.Pop());
                        snapshot._objects.Add(copy.gameObject);
                        snapshot._objectRegions.Add(copy.gameObject, _objectRegions[item]);
                    }
                    // Meshes and materials in this snapshot are borrowed from the live preview.
                    // Only the snapshot objects belong to this snapshot's cleanup.
                }
                else snapshot.Build(results, graphName);

                snapshot._exportScene = EditorSceneManager.NewPreviewScene();
                SceneManager.MoveGameObjectToScene(snapshot._root, snapshot._exportScene);
                snapshot._root.hideFlags = HideFlags.HideAndDontSave;
                snapshot.ShowAll();
                return snapshot;
            }
            catch { snapshot.Dispose(); throw; }
        }

        internal string SaveImage(IReadOnlyList<TerraKitGeneratedRegion> results, string graphName)
        {
            using (var snapshot = PrepareExport(results, graphName))
            {
                var renderers = snapshot._objects.Select(item => item.GetComponent<MeshRenderer>()).ToArray();
                return TerraKitMeshImageExport.Save(renderers, graphName);
            }
        }

        internal bool SavePrefab(IReadOnlyList<TerraKitGeneratedRegion> results, string graphName)
        {
            var destination = _root != null ? _root.scene : SceneManager.GetActiveScene();
            using (var snapshot = PrepareExport(results, graphName))
            {
                if (snapshot._objects.Count == 0)
                    throw new InvalidOperationException("The map has no Mesh outputs to save.");
                return snapshot.SaveEntries(snapshot._objects.ToArray(), graphName + "_Map", destination,
                    "Save Map Prefab", "Save all Regions with their Meshes and materials as one map Prefab.", true);
            }
        }

        internal bool SaveRegionMeshes(IReadOnlyList<TerraKitGeneratedRegion> results, string graphName,
            int regionIndex)
        {
            if (results == null || regionIndex < 0 || regionIndex >= results.Count)
                throw new ArgumentOutOfRangeException(nameof(regionIndex), "Select a generated Region first.");
            if (!results[regionIndex].Resources.Any(resource => resource.Mesh != null))
                throw new InvalidOperationException("The selected Region has no Mesh outputs to save.");

            using (var snapshot = PrepareExport(results, graphName))
            {
                var objects = snapshot.ObjectsForRegion(regionIndex);
                var request = results[regionIndex].Request;
                string name = graphName + "_Region_" + request.RegionX + "_" + request.RegionY +
                    (request.Is3D ? "_" + request.RegionZ : "");
                // A companion Prefab keeps material slots bound to the saved Mesh assets.
                return snapshot.SaveEntries(objects, name, default,
                    "Save Region Meshes and Materials",
                    "Save all Mesh outputs in the selected Region with their materials. A Region Prefab preserves material assignments.",
                    false);
            }
        }

        private GameObject[] ObjectsForRegion(int regionIndex)
        {
            return _objects.Where(item => _objectRegions[item] == regionIndex).ToArray();
        }

        private bool SaveEntries(GameObject[] objects, string suggestedName, Scene destination,
            string dialogTitle, string description, bool placeInScene)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                dialogTitle, suggestedName, "prefab", description);
            if (string.IsNullOrEmpty(path)) return false;

            return SaveEntriesToPath(objects, path, destination, placeInScene);
        }

        private bool SaveEntriesToPath(GameObject[] objects, string path, Scene destination, bool placeInScene)
        {
            // Each save owns new assets; existing exports and their instances remain unchanged.
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            string prefabPath = path;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string folder = AssetDatabase.GenerateUniqueAssetPath(parent + "/" +
                Path.GetFileNameWithoutExtension(path) + "_Resources");
            var savedPaths = new List<string>();
            var meshes = new Dictionary<Mesh, Mesh>();
            var materials = new Dictionary<Material, Material>();
            var textures = new Dictionary<Texture, Texture>();
            var ancestors = new Dictionary<Transform, Transform>();
            GameObject export = null;
            GameObject instance = null;
            try
            {
                string guid = AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
                if (string.IsNullOrEmpty(guid)) throw new IOException("Could not create the asset folder.");
                savedPaths.Add(folder);
                export = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
                export.hideFlags = HideFlags.HideAndDontSave;
                foreach (var source in objects)
                {
                    var sourceMesh = source.GetComponent<MeshFilter>().sharedMesh;
                    if (sourceMesh == null) throw new InvalidOperationException("A preview Mesh is missing.");
                    if (!meshes.TryGetValue(sourceMesh, out var mesh))
                    {
                        mesh = Object.Instantiate(sourceMesh);
                        mesh.hideFlags = HideFlags.None;
                        string meshPath = folder + "/Mesh_" + (meshes.Count + 1) + ".asset";
                        SaveNewAsset(mesh, meshPath, savedPaths);
                        meshes.Add(sourceMesh, mesh);
                    }

                    var child = new GameObject(source.name);
                    child.transform.SetParent(CopyAncestors(source.transform.parent,
                        export.transform, ancestors), false);
                    CopyLocalTransform(source.transform, child.transform);
                    child.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var sourceRenderer = source.GetComponent<MeshRenderer>();
                    var renderer = child.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = sourceRenderer.sharedMaterials
                        .Select(material => SaveMaterial(material, folder, materials, textures, savedPaths)).ToArray();
                    renderer.enabled = sourceRenderer.enabled;
                    renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
                    renderer.receiveShadows = sourceRenderer.receiveShadows;
                }

                export.hideFlags = HideFlags.None;
                savedPaths.Add(prefabPath);
                var prefab = PrefabUtility.SaveAsPrefabAsset(export, prefabPath);
                if (prefab == null) throw new IOException("Could not save the prefab.");
                AssetDatabase.SaveAssets();

                if (placeInScene)
                {
                    // Only Save Prefab places a complete map instance at the Unity origin.
                    instance = PrefabUtility.InstantiatePrefab(prefab, destination) as GameObject;
                    if (instance == null) throw new IOException("Could not place the saved prefab in the scene.");
                    instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    instance.transform.localScale = Vector3.one;
                    Undo.RegisterCreatedObjectUndo(instance, "Save TerraKit Prefab");
                    EditorSceneManager.MarkSceneDirty(instance.scene);
                    Selection.activeGameObject = instance;
                }
                else Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
                SceneView.RepaintAll();
            }
            catch
            {
                if (instance != null) Object.DestroyImmediate(instance);
                for (int i = savedPaths.Count - 1; i >= 0; i--) AssetDatabase.DeleteAsset(savedPaths[i]);
                throw;
            }
            finally { if (export != null) Object.DestroyImmediate(export); }
            return true;
        }

        private Transform CopyAncestors(Transform source, Transform root,
            Dictionary<Transform, Transform> copies)
        {
            if (source == null || source == _root.transform) return root;
            if (copies.TryGetValue(source, out var copy)) return copy;
            var parent = CopyAncestors(source.parent, root, copies);
            // Copy generated region groups below the permanent map root.
            copy = new GameObject(source.name).transform;
            copy.SetParent(parent, false);
            CopyLocalTransform(source, copy);
            copies.Add(source, copy);
            return copy;
        }

        private static void CopyLocalTransform(Transform source, Transform target)
        {
            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
        }

        private static Material SaveMaterial(Material source, string folder,
            Dictionary<Material, Material> materials, Dictionary<Texture, Texture> textures,
            List<string> savedPaths)
        {
            if (source == null) return null;
            if (materials.TryGetValue(source, out var saved)) return saved;
            saved = new Material(source) { name = source.name, hideFlags = HideFlags.None };
            SaveNewAsset(saved, folder + "/Material_" + (materials.Count + 1) + ".mat", savedPaths);
            materials.Add(source, saved);
            foreach (string property in saved.GetTexturePropertyNames())
            {
                var texture = saved.GetTexture(property);
                if (texture == null || EditorUtility.IsPersistent(texture)) continue;
                if (!textures.TryGetValue(texture, out var savedTexture))
                {
                    // Temporary render targets cannot be referenced by a persistent material.
                    if (texture is RenderTexture)
                        throw new InvalidOperationException("Save the material's RenderTexture as a texture asset first.");
                    savedTexture = Object.Instantiate(texture);
                    savedTexture.hideFlags = HideFlags.None;
                    SaveNewAsset(savedTexture, folder + "/Texture_" + (textures.Count + 1) + ".asset", savedPaths);
                    textures.Add(texture, savedTexture);
                }
                saved.SetTexture(property, savedTexture);
            }
            EditorUtility.SetDirty(saved);
            return saved;
        }

        private static void SaveNewAsset(Object asset, string path, List<string> savedPaths)
        {
            savedPaths.Add(path);
            try
            {
                AssetDatabase.CreateAsset(asset, path);
                if (AssetDatabase.GetAssetPath(asset) != path)
                    throw new IOException("Could not save asset: " + path);
            }
            catch
            {
                if (!EditorUtility.IsPersistent(asset)) Object.DestroyImmediate(asset);
                throw;
            }
        }

        public void Dispose()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            foreach (var mesh in _meshes) if (mesh != null) Object.DestroyImmediate(mesh);
            _objects.Clear(); _objectRegions.Clear(); _meshes.Clear(); _root = null; _results = null;
            if (_exportScene.IsValid()) EditorSceneManager.ClosePreviewScene(_exportScene);
            _exportScene = default;
        }
    }
}
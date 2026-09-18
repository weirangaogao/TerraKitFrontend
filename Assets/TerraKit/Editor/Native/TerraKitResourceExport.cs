using System;
using System.IO;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TerraKit.Editor
{
    internal static class TerraKitResourceExport
    {
        [Serializable]
        private sealed class ExportFile
        {
            public int formatVersion = 1;
            public string resourceType, nodeId, portId, graphName, graphSnapshotJson;
            public string generatedAt, seed, regionX, regionY, regionZ;
            public bool is3D;
            public int lod;
            public uint cellWidth, cellHeight, cellDepth;
            public double spacingX, spacingY, spacingZ;
            public string gridStorage = "x + width * (y + height * z); sampling 1=points, 2=cells";
            public string meshCoordinates = "local vertices + double precision worldOrigin; no axis conversion";
            public TerraKitGridData grid;
            public TerraKitDouble3 worldOrigin;
            public Vector3[] positions, normals;
            public Vector2[] texcoords;
            public int[] indices;
        }

        internal static string ToJson(TerraKitGeneratedResource resource, TerraKitBackendGenerationRequest request,
            string graphName, string graphSnapshot, string generatedAt)
        {
            var data = new ExportFile
            {
                resourceType = resource.Kind.ToString(), nodeId = resource.Output.NodeId, portId = resource.Output.PortId,
                graphName = graphName, graphSnapshotJson = graphSnapshot, generatedAt = generatedAt,
                seed = request.Seed.ToString(CultureInfo.InvariantCulture),
                regionX = request.RegionX.ToString(CultureInfo.InvariantCulture),
                regionY = request.RegionY.ToString(CultureInfo.InvariantCulture),
                regionZ = request.RegionZ.ToString(CultureInfo.InvariantCulture), is3D = request.Is3D,
                lod = request.LodLevel, cellWidth = request.CellWidth, cellHeight = request.CellHeight,
                cellDepth = request.CellDepth, spacingX = request.SpacingX, spacingY = request.SpacingY, spacingZ = request.SpacingZ,
                grid = resource.Grid
            };
            if (resource.Mesh != null)
            {
                data.worldOrigin = resource.Mesh.WorldOrigin; data.positions = resource.Mesh.Positions;
                data.normals = resource.Mesh.Normals; data.indices = resource.Mesh.Indices; data.texcoords = resource.Mesh.Texcoords;
            }
            return JsonUtility.ToJson(data, true);
        }

        internal static Mesh CreateMesh(TerraKitGeneratedMeshData source)
        {
            var mesh = new Mesh { name = "TerraKit Generated Mesh" };
            if (source.Positions.Length > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = source.Positions; mesh.triangles = source.Indices;
            if (source.Normals.Length == source.Positions.Length) mesh.normals = source.Normals;
            else mesh.RecalculateNormals();
            if (source.Texcoords.Length == source.Positions.Length) mesh.uv = source.Texcoords;
            mesh.RecalculateBounds();
            return mesh;
        }

        internal static void SaveMesh(TerraKitGeneratedMeshData source, string suggestedName)
        {
            string path = EditorUtility.SaveFilePanelInProject("Save Selected Mesh", suggestedName, "asset", "Save the selected output.");
            if (string.IsNullOrEmpty(path)) return;
            var existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null && !(existing is Mesh)) throw new InvalidOperationException("This path contains a non-Mesh asset.");
            var mesh = CreateMesh(source);
            try
            {
                mesh.name = Path.GetFileNameWithoutExtension(path);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(mesh, existing);
                    EditorUtility.SetDirty(existing);
                }
                else { AssetDatabase.CreateAsset(mesh, path); mesh = null; }
                AssetDatabase.SaveAssets();
            }
            finally { if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh); }
        }

        internal static void Range(TerraKitGridData grid, out float minimum, out float maximum)
        {
            minimum = float.PositiveInfinity; maximum = float.NegativeInfinity;
            if (grid.Values != null)
                foreach (float value in grid.Values)
                    if (!float.IsNaN(value) && !float.IsInfinity(value))
                    { minimum = Mathf.Min(minimum, value); maximum = Mathf.Max(maximum, value); }
            if (float.IsPositiveInfinity(minimum)) minimum = maximum = 0;
        }

        internal static Texture2D Preview(TerraKitGridData grid, int slice, float minimum, float maximum)
        {
            // Preview is bounded; exported JSON always retains every original sample.
            int width = Math.Min(grid.Width, 512), height = Math.Min(grid.Height, 512);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            var colors = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int gx = width == 1 ? 0 : (int)((long)x * (grid.Width - 1) / (width - 1));
                    int gy = height == 1 ? 0 : (int)((long)y * (grid.Height - 1) / (height - 1));
                    int index = gx + grid.Width * (gy + grid.Height * slice);
                    Color32 color;
                    if (grid.VoxelIds != null)
                    {
                        uint hash = unchecked(grid.VoxelIds[index] * 2654435761u);
                        color = new Color32((byte)(64 + (hash & 127)), (byte)(64 + ((hash >> 8) & 127)), (byte)(64 + ((hash >> 16) & 127)), 255);
                    }
                    else
                    {
                        float value = grid.Values[index];
                        double span = (double)maximum - minimum;
                        byte grey = span > 0 ? (byte)Math.Round(Math.Max(0, Math.Min(1, ((double)value - minimum) / span)) * 255) : (byte)128;
                        color = float.IsNaN(value) || float.IsInfinity(value) ? new Color32(255, 0, 255, 255) : new Color32(grey, grey, grey, 255);
                    }
                    colors[x + width * y] = color;
                }
            texture.SetPixels32(colors); texture.Apply(); return texture;
        }
    }
}
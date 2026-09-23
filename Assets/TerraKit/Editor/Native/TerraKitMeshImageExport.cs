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
    internal static class TerraKitMeshImageExport
    {
        internal static string Save(IReadOnlyList<MeshRenderer> renderers, string graphName)
        {
            var visible = renderers.Where(HasGeometry).ToArray();
            if (visible.Length == 0)
                throw new InvalidOperationException("No Mesh geometry is available in the selected scope.");

            string filename = (graphName ?? "TerraKit") + "_Preview";
            foreach (char character in Path.GetInvalidFileNameChars()) filename = filename.Replace(character, '_');
            string path = EditorUtility.SaveFilePanel("Export Mesh Preview Image", "", filename, "png");
            if (string.IsNullOrEmpty(path)) return null;

            Texture2D image = null;
            try
            {
                // Use a fixed image aspect ratio independent of all editor cameras.
                int width = Mathf.Max(1, Mathf.Min(2048, Mathf.FloorToInt(SystemInfo.maxTextureSize /
                    Mathf.Max(1, EditorGUIUtility.pixelsPerPoint))));
                int height = Mathf.Max(1, Mathf.RoundToInt(width * 0.75f));
                image = Render(visible, width, height);
                File.WriteAllBytes(path, image.EncodeToPNG());
                return path;
            }
            finally
            {
                if (image != null) Object.DestroyImmediate(image);
            }
        }

        private static bool HasGeometry(MeshRenderer renderer)
        {
            // The caller supplies the exact export scope; scene visibility does not select outputs.
            return renderer != null && renderer.GetComponent<MeshFilter>()?.sharedMesh != null;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static void CalculateFraming(Vector3 extents, Vector3 right, Vector3 up,
            Vector3 forward, float aspect, out float size, out float distance, out float farClip)
        {
            // Project all bounding-box extents into the fixed camera basis and leave a 12% margin.
            float horizontal = Mathf.Abs(right.x) * extents.x + Mathf.Abs(right.y) * extents.y + Mathf.Abs(right.z) * extents.z;
            float vertical = Mathf.Abs(up.x) * extents.x + Mathf.Abs(up.y) * extents.y + Mathf.Abs(up.z) * extents.z;
            float depth = Mathf.Abs(forward.x) * extents.x + Mathf.Abs(forward.y) * extents.y + Mathf.Abs(forward.z) * extents.z;
            size = Mathf.Max(0.01f, Mathf.Max(vertical, horizontal / aspect) * 1.12f);
            distance = depth + 2;
            farClip = distance + depth + 2;
        }

        internal static Texture2D Render(IReadOnlyList<MeshRenderer> renderers, int width, int height)
        {
            if (renderers == null || renderers.Count == 0)
                throw new ArgumentException("No Mesh renderers were supplied.", nameof(renderers));
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));

            var visible = renderers.Where(HasGeometry).ToArray();
            if (visible.Length == 0)
                throw new InvalidOperationException("No visible Mesh geometry is available to export.");
            Bounds bounds = visible[0].bounds;
            foreach (var renderer in visible)
            {
                if (!IsFinite(renderer.bounds.center) || !IsFinite(renderer.bounds.extents))
                    throw new InvalidOperationException("A Mesh has invalid bounds and cannot be framed.");
                bounds.Encapsulate(renderer.bounds);
            }
            if (!IsFinite(bounds.center) || !IsFinite(bounds.extents))
                throw new InvalidOperationException("Mesh bounds exceed the supported image export range.");
            float extent = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));
            float scale = 1f / Mathf.Max(extent, 0.0001f);
            Vector3 normalizedExtents = bounds.extents * scale;

            var previousTarget = RenderTexture.active;
            var preview = new PreviewRenderUtility();
            Texture2D image = null;
            bool completed = false;
            try
            {
                // The utility camera belongs to an isolated preview scene, not the user's scene.
                var camera = preview.camera;
                Quaternion rotation = Quaternion.Euler(35, 45, 0);
                Vector3 forward = rotation * Vector3.forward;
                CalculateFraming(normalizedExtents, rotation * Vector3.right, rotation * Vector3.up,
                    forward, (float)width / height, out float size, out float distance, out float farClip);
                camera.transform.SetPositionAndRotation(-forward * distance, rotation);
                camera.orthographic = true;
                camera.orthographicSize = size;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = farClip;
                camera.aspect = (float)width / height;
                camera.ResetWorldToCameraMatrix();
                camera.ResetProjectionMatrix();
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.useOcclusionCulling = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                Color background = new Color32(32, 32, 32, 255);
                camera.backgroundColor = QualitySettings.activeColorSpace == ColorSpace.Linear
                    ? background.linear : background;
                camera.cullingMask = 1;
                preview.ambientColor = new Color(0.3f, 0.3f, 0.3f, 1);
                var lights = preview.lights;
                lights[0].color = Color.white;
                lights[0].intensity = 1.1f;
                lights[0].transform.rotation = rotation * Quaternion.Euler(35, -30, 0);
                lights[1].color = Color.white;
                lights[1].intensity = 0.5f;
                lights[1].transform.rotation = rotation * Quaternion.Euler(-25, 135, 0);

                preview.BeginStaticPreview(new Rect(0, 0, width, height));
                try
                {
                    int drawn = 0;
                    var properties = new MaterialPropertyBlock();
                    var perMaterial = new MaterialPropertyBlock();
                    foreach (var renderer in visible)
                    {
                        if (!HasGeometry(renderer)) continue;
                        Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                        // Rebase only the draw matrix to avoid large world-coordinate clipping and precision loss.
                        Matrix4x4 matrix = renderer.localToWorldMatrix;
                        Vector3 position = new Vector3(matrix.m03, matrix.m13, matrix.m23) - bounds.center;
                        matrix.m03 = position.x;
                        matrix.m13 = position.y;
                        matrix.m23 = position.z;
                        matrix = Matrix4x4.Scale(Vector3.one * scale) * matrix;
                        Material[] materials = renderer.sharedMaterials;
                        if (materials.Length == 0 || materials.Any(material => material == null))
                            throw new InvalidOperationException("A visible Mesh has no material. Assign a material before exporting.");
                        properties.Clear();
                        renderer.GetPropertyBlock(properties);
                        for (int index = 0; index < Math.Max(mesh.subMeshCount, materials.Length); index++)
                        {
                            if (mesh.subMeshCount == 0) break;
                            int materialIndex = Math.Min(index, materials.Length - 1);
                            perMaterial.Clear();
                            renderer.GetPropertyBlock(perMaterial, materialIndex);
                            // A common translation and scale preserve relative positions without changing scene objects.
                            Graphics.DrawMesh(mesh, matrix, materials[materialIndex], 0,
                                camera, Math.Min(index, mesh.subMeshCount - 1),
                                perMaterial.isEmpty ? properties : perMaterial,
                                ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
                            drawn++;
                        }
                    }
                    if (drawn == 0) throw new InvalidOperationException("No visible Mesh geometry is available to export.");
                    preview.Render(allowScriptableRenderPipeline: true, updatefov: false);
                }
                finally
                {
                    // EndStaticPreview restores render state and converts the readback for PNG display.
                    image = preview.EndStaticPreview();
                }
                completed = true;
                return image;
            }
            finally
            {
                try { preview.Cleanup(); }
                finally
                {
                    RenderTexture.active = previousTarget;
                    if (!completed && image != null) Object.DestroyImmediate(image);
                }
            }
        }
    }
}
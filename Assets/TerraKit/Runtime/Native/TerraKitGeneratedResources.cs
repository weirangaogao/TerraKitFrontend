using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TerraKit
{
    [Serializable, StructLayout(LayoutKind.Sequential)]
    public struct TerraKitDouble3
    {
        public double X, Y, Z;
        public TerraKitDouble3(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    [Serializable, StructLayout(LayoutKind.Sequential)]
    public struct TerraKitGridTransform2
    {
        public TerraKitDouble3 Origin, AxisX, AxisY;
    }

    [Serializable, StructLayout(LayoutKind.Sequential)]
    public struct TerraKitGridTransform3
    {
        public TerraKitDouble3 Origin, AxisX, AxisY, AxisZ;
    }

    public sealed class TerraKitOutputDescriptor
    {
        public string NodeId { get; private set; }
        public string NodeName { get; private set; }
        public string PortId { get; private set; }
        public string PortName { get; private set; }
        public TerraKitBackendResourceKind Kind { get; private set; }
        public ulong ResourceKey { get; private set; }
        public string Address { get { return NodeId + "\n" + PortId; } }
        public string Label { get { return NodeName + " / " + PortName + " (" + Kind + ")"; } }
        internal TerraKitOutputDescriptor(string nodeId, string nodeName, string portId,
            string portName, TerraKitBackendResourceKind kind, ulong key)
        {
            NodeId = nodeId; NodeName = nodeName; PortId = portId;
            PortName = portName; Kind = kind; ResourceKey = key;
        }
    }

    /// <summary>Original samples, x fastest: x + width * (y + height * z).
    /// Sampling: 1 = points, 2 = cells. Transforms and voxel IDs retain native precision.</summary>
    [Serializable]
    public sealed class TerraKitGridData
    {
        public int Width, Height, Depth;
        public int Sampling;
        public TerraKitGridTransform3 Transform;
        public TerraKitDouble3 HeightAxis;
        public float[] Values;
        public uint[] VoxelIds;
        public int Count { get { return Values != null ? Values.Length : VoxelIds.Length; } }
    }

    public sealed class TerraKitGeneratedResource
    {
        public TerraKitOutputDescriptor Output { get; private set; }
        public TerraKitBackendResourceKind Kind { get { return Output.Kind; } }
        public TerraKitGridData Grid { get; private set; }
        public TerraKitGeneratedMeshData Mesh { get; private set; }
        internal TerraKitGeneratedResource(TerraKitOutputDescriptor output, TerraKitGridData grid,
            TerraKitGeneratedMeshData mesh = null) { Output = output; Grid = grid; Mesh = mesh; }
    }

    public sealed class TerraKitGeneratedRegion
    {
        public TerraKitBackendGenerationRequest Request { get; private set; }
        public IReadOnlyList<TerraKitGeneratedResource> Resources { get; private set; }
        internal TerraKitGeneratedRegion(TerraKitBackendGenerationRequest request,
            IReadOnlyList<TerraKitGeneratedResource> resources) { Request = request; Resources = resources; }
    }

    internal static class TerraKitResourceReader
    {
        internal static TerraKitGeneratedResource Read(IntPtr result, TerraKitOutputDescriptor output)
        {
            int actual;
            TerraKitNativeCall.ThrowIfFailed(TerraKitNativeResultMethods.GetResourceKind(result, output.ResourceKey, out actual),
                "tk_generation_result_get_resource_kind");
            if (actual != (int)output.Kind)
                throw new InvalidOperationException("Output type differs from its schema: " + output.Label);
            switch (output.Kind)
            {
                case TerraKitBackendResourceKind.Mesh:
                    TerraKitNativeTerrainMeshView mesh;
                    TerraKitNativeCall.ThrowIfFailed(TerraKitNativeResultMethods.GetMesh(result, output.ResourceKey, out mesh),
                        "tk_generation_result_get_mesh");
                    return new TerraKitGeneratedResource(output, null, TerraKitBackendGraphRunner.CopyMesh(mesh));
                case TerraKitBackendResourceKind.HeightField:
                    TerraKitNativeHeightFieldView height;
                    TerraKitNativeCall.ThrowIfFailed(TerraKitNativeResultMethods.GetHeight(result, output.ResourceKey, out height),
                        "tk_generation_result_get_height_field");
                    return new TerraKitGeneratedResource(output, CopyHeight(height));
                case TerraKitBackendResourceKind.DensityField:
                case TerraKitBackendResourceKind.VoxelVolume:
                    TerraKitNativeVolumeView volume;
                    int status = output.Kind == TerraKitBackendResourceKind.DensityField
                        ? TerraKitNativeResultMethods.GetDensity(result, output.ResourceKey, out volume)
                        : TerraKitNativeResultMethods.GetVoxels(result, output.ResourceKey, out volume);
                    TerraKitNativeCall.ThrowIfFailed(status, "Read " + output.Kind);
                    return new TerraKitGeneratedResource(output, CopyVolume(volume, output.Kind));
                default:
                    throw new NotSupportedException("Unsupported resource type: " + output.Kind);
            }
        }

        internal static TerraKitGridData CopyHeight(TerraKitNativeHeightFieldView view)
        {
            int count = CheckGrid(view.Width, view.Height, 1, view.Sampling, view.Values, view.ValueCount);
            var values = new float[count];
            Marshal.Copy(view.Values, values, 0, count);
            return new TerraKitGridData
            {
                Width = (int)view.Width, Height = (int)view.Height, Depth = 1,
                Sampling = view.Sampling, HeightAxis = view.HeightAxis, Values = values,
                Transform = new TerraKitGridTransform3
                { Origin = view.Transform.Origin, AxisX = view.Transform.AxisX, AxisY = view.Transform.AxisY }
            };
        }

        internal static TerraKitGridData CopyVolume(TerraKitNativeVolumeView view, TerraKitBackendResourceKind kind)
        {
            int count = CheckGrid(view.Width, view.Height, view.Depth, view.Sampling, view.Values, view.ValueCount);
            var grid = new TerraKitGridData
            {
                Width = (int)view.Width, Height = (int)view.Height, Depth = (int)view.Depth,
                Sampling = view.Sampling, Transform = view.Transform
            };
            if (kind == TerraKitBackendResourceKind.VoxelVolume)
            {
                var raw = new int[count];
                Marshal.Copy(view.Values, raw, 0, count);
                grid.VoxelIds = new uint[count];
                Buffer.BlockCopy(raw, 0, grid.VoxelIds, 0, checked(count * sizeof(uint)));
            }
            else
            {
                grid.Values = new float[count];
                Marshal.Copy(view.Values, grid.Values, 0, count);
            }
            return grid;
        }

        private static int CheckGrid(uint width, uint height, uint depth, int sampling, IntPtr data, UIntPtr length)
        {
            ulong expected = checked((ulong)width * height * depth);
            if (width == 0 || height == 0 || depth == 0 || expected > int.MaxValue / 4 ||
                length.ToUInt64() != expected || data == IntPtr.Zero || (sampling != 1 && sampling != 2))
                throw new InvalidOperationException("Backend returned an invalid or excessively large grid buffer.");
            return (int)expected;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerraKitNativeHeightFieldView
    {
        public uint Width, Height;
        public int Sampling;
        public uint Reserved;
        public TerraKitGridTransform2 Transform;
        public TerraKitDouble3 HeightAxis;
        public IntPtr Values;
        public UIntPtr ValueCount;
    }

    // Density and voxel views have identical layouts; their sample element types differ.
    [StructLayout(LayoutKind.Sequential)]
    internal struct TerraKitNativeVolumeView
    {
        public uint Width, Height, Depth, ReservedExtent;
        public int Sampling;
        public uint ReservedSampling;
        public TerraKitGridTransform3 Transform;
        public IntPtr Values;
        public UIntPtr ValueCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerraKitNativeRegionLayout3D
    {
        public uint CellWidth, CellHeight, CellDepth, Reserved;
        public TerraKitNativeVector3Float64 BaseSpacing;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerraKitNativeGenerationRequest3D
    {
        public ulong Seed;
        public long RegionX, RegionY, RegionZ;
        public ushort LodLevel;
        public byte Reserved0, Reserved1, Reserved2, Reserved3, Reserved4, Reserved5;
    }

    public sealed partial class TerraKitBackendGraphRunner
    {
        private static void CreateRuntime(ref IntPtr pipeline, TerraKitBackendGenerationRequest request, out IntPtr runtime)
        {
            if (request.Is3D)
            {
                var layout = new TerraKitNativeRegionLayout3D
                {
                    CellWidth = request.CellWidth, CellHeight = request.CellHeight, CellDepth = request.CellDepth,
                    BaseSpacing = new TerraKitNativeVector3Float64 { X = request.SpacingX, Y = request.SpacingY, Z = request.SpacingZ }
                };
                TerraKitNativeCall.ThrowIfFailed(TerraKitNativeRuntimeMethods.Create3D(ref pipeline, ref layout, out runtime), "tk_runtime_create_3d");
            }
            else
            {
                var layout = new TerraKitNativeRegionLayout2D
                {
                    CellWidth = request.CellWidth, CellHeight = request.CellHeight,
                    BaseSpacing = new TerraKitNativeVector2Float64 { X = request.SpacingX, Y = request.SpacingY }
                };
                TerraKitNativeCall.ThrowIfFailed(TerraKitNativeRuntimeMethods.Create2D(ref pipeline, ref layout, out runtime), "tk_runtime_create_2d");
            }
        }

        private static void GenerateRegion(IntPtr runtime, TerraKitBackendGenerationRequest settings, out IntPtr result)
        {
            if (settings.Is3D)
            {
                var request = new TerraKitNativeGenerationRequest3D
                { Seed = settings.Seed, RegionX = settings.RegionX, RegionY = settings.RegionY, RegionZ = settings.RegionZ, LodLevel = settings.LodLevel };
                TerraKitNativeCall.ThrowIfFailed(TerraKitNativeRuntimeMethods.Generate3D(runtime, ref request, out result), "tk_runtime_generate_3d");
            }
            else
            {
                var request = new TerraKitNativeGenerationRequest2D
                { Seed = settings.Seed, RegionX = settings.RegionX, RegionY = settings.RegionY, LodLevel = settings.LodLevel };
                TerraKitNativeCall.ThrowIfFailed(TerraKitNativeRuntimeMethods.Generate2D(runtime, ref request, out result), "tk_runtime_generate_2d");
            }
        }
    }

    internal static partial class TerraKitNativeRuntimeMethods
    {
        [DllImport(LibraryName, EntryPoint = "tk_runtime_create_3d", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int Create3D(ref IntPtr pipeline, ref TerraKitNativeRegionLayout3D layout, out IntPtr runtime);
        [DllImport(LibraryName, EntryPoint = "tk_runtime_generate_3d", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int Generate3D(IntPtr runtime, ref TerraKitNativeGenerationRequest3D request, out IntPtr result);
    }

    internal static partial class TerraKitNativeResultMethods
    {
        [DllImport(LibraryName, EntryPoint = "tk_generation_result_get_resource_kind", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int GetResourceKind(IntPtr result, ulong key, out int kind);
        [DllImport(LibraryName, EntryPoint = "tk_generation_result_get_height_field", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int GetHeight(IntPtr result, ulong key, out TerraKitNativeHeightFieldView view);
        [DllImport(LibraryName, EntryPoint = "tk_generation_result_get_density_field", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int GetDensity(IntPtr result, ulong key, out TerraKitNativeVolumeView view);
        [DllImport(LibraryName, EntryPoint = "tk_generation_result_get_voxel_volume", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int GetVoxels(IntPtr result, ulong key, out TerraKitNativeVolumeView view);
    }
}
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

namespace TerraKit
{
    // Convert graph into native pipeline
    public sealed class TerraKitGeneratedMeshData
    {
        public Vector3 Origin { get { return new Vector3((float)WorldOrigin.X, (float)WorldOrigin.Y, (float)WorldOrigin.Z); } }
        public TerraKitDouble3 WorldOrigin { get; private set; }
        public Vector3[] Positions { get; private set; }
        public int[] Indices { get; private set; }
        public Vector3[] Normals { get; private set; }
        public Vector2[] Texcoords { get; private set; }

        internal TerraKitGeneratedMeshData(
            TerraKitDouble3 origin,
            Vector3[] positions,
            int[] indices,
            Vector3[] normals,
            Vector2[] texcoords)
        {
            WorldOrigin = origin;
            Positions = positions;
            Indices = indices;
            Normals = normals;
            Texcoords = texcoords;
        }
    }

    // External runtime request values used when generating one backend region.
    // These settings are not pipeline stages and are separate from the future Plugin SDK.
    public sealed class TerraKitBackendGenerationRequest
    {
        public bool Is3D { get; private set; }
        public long RegionZ { get; private set; }
        public uint CellDepth { get; private set; }
        public double SpacingZ { get; private set; }
        public ulong Seed { get; private set; }
        public long RegionX { get; private set; }
        public long RegionY { get; private set; }
        public ushort LodLevel { get; private set; }
        public uint CellWidth { get; private set; }
        public uint CellHeight { get; private set; }
        public double SpacingX { get; private set; }
        public double SpacingY { get; private set; }

        public static TerraKitBackendGenerationRequest Default
        {
            get
            {
                return new TerraKitBackendGenerationRequest(
                    12345, 0, 0, 0, 16, 16, 1, 1);
            }
        }

        public TerraKitBackendGenerationRequest(
            ulong seed,
            long regionX,
            long regionY,
            ushort lodLevel,
            uint cellWidth,
            uint cellHeight,
            double spacingX,
            double spacingY,
            bool is3D = false,
            long regionZ = 0,
            uint cellDepth = 1,
            double spacingZ = 1)
        {
            if (is3D && (cellDepth == 0 || double.IsNaN(spacingZ) ||
                double.IsInfinity(spacingZ) || spacingZ <= 0))
                throw new ArgumentOutOfRangeException("cellDepth", "3D depth and spacing must be positive and finite.");
            Is3D = is3D;
            RegionZ = regionZ;
            CellDepth = cellDepth;
            SpacingZ = spacingZ;
            if (cellWidth == 0 || cellHeight == 0)
            {
                throw new ArgumentOutOfRangeException(
                    "cellWidth", "Backend cell dimensions must be greater than zero.");
            }
            if (double.IsNaN(spacingX) || double.IsInfinity(spacingX) || spacingX <= 0 ||
                double.IsNaN(spacingY) || double.IsInfinity(spacingY) || spacingY <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    "spacingX", "Backend spacing values must be finite and greater than zero.");
            }

            Seed = seed;
            RegionX = regionX;
            RegionY = regionY;
            LodLevel = lodLevel;
            CellWidth = cellWidth;
            CellHeight = cellHeight;
            SpacingX = spacingX;
            SpacingY = spacingY;
        }
    }

    // Translates backend graph nodes into a native TerraKit pipeline and runs 2D or 3D regions. Every declared output is copied into managed memory.
    public sealed partial class TerraKitBackendGraphRunner
    {
        public TerraKitGeneratedRegion GenerateResources(TerraKitGraphAsset graph, TerraKitBackendGenerationRequest request)
        {
            return GenerateResources(graph, new[] { request })[0];
        }

        public IReadOnlyList<TerraKitGeneratedRegion> GenerateResources(
            TerraKitGraphAsset graph,
            IReadOnlyList<TerraKitBackendGenerationRequest> generationRequests,
            Func<int, int, bool> shouldCancel = null)
        {
            if (generationRequests == null)
            {
                throw new ArgumentNullException("generationRequests");
            }
            if (generationRequests.Count == 0)
            {
                throw new ArgumentException(
                    "At least one backend generation request is required.",
                    "generationRequests");
            }

            TerraKitBackendGenerationRequest generationRequest = generationRequests[0];
            if (generationRequest == null)
            {
                throw new ArgumentException(
                    "Backend generation requests cannot contain null entries.",
                    "generationRequests");
            }
            for (int requestIndex = 1; requestIndex < generationRequests.Count; requestIndex++)
            {
                TerraKitBackendGenerationRequest candidate = generationRequests[requestIndex];
                if (candidate == null)
                {
                    throw new ArgumentException(
                        "Backend generation requests cannot contain null entries.",
                        "generationRequests");
                }
                if (candidate.Is3D != generationRequest.Is3D ||
                    (generationRequest.Is3D &&
                        (candidate.CellDepth != generationRequest.CellDepth ||
                         candidate.SpacingZ != generationRequest.SpacingZ)) ||
                    candidate.CellWidth != generationRequest.CellWidth ||
                    candidate.CellHeight != generationRequest.CellHeight ||
                    candidate.SpacingX != generationRequest.SpacingX ||
                    candidate.SpacingY != generationRequest.SpacingY)
                {
                    throw new ArgumentException(
                        "Requests generated by one runtime must share the same cell layout and spacing.",
                        "generationRequests");
                }
            }

            // The callback receives completed and total region counts; true requests cancellation.
            if (shouldCancel != null && shouldCancel(0, generationRequests.Count))
                throw new OperationCanceledException("Map generation cancelled.");

            TerraKitNativeStatus availability = TerraKitNativeLibrary.CheckAvailability();
            if (!availability.IsAvailable)
            {
                throw new TerraKitNativeException(
                    availability.StatusCode,
                    availability.Summary + " " + availability.Details);
            }

            var compileResult = new TerraKitGraphCompiler().Compile(graph);
            if (!compileResult.Success)
            {
                throw new TerraKitGraphValidationException(compileResult);
            }

            foreach (TerraKitNodeData node in compileResult.ExecutionOrder)
            {
                if (!TerraKitNodeRegistry.IsBackendExecutable(node.typeId))
                {
                    throw new NotSupportedException(
                        "Backend generation requires stages from the installed backend registry. " +
                        "Unsupported node: " + node.DisplayLabel +
                        " (" + node.typeId + ").");
                }
            }

            IReadOnlyDictionary<string, TerraKitBackendStageSchema> schemas =
                TerraKitNodeRegistry.BackendSchemas
                    .ToDictionary(schema => schema.TypeId, schema => schema);

            TerraKitBackendExecutionAbiLayout.Validate();

            IntPtr registry = IntPtr.Zero;
            IntPtr assembler = IntPtr.Zero;
            IntPtr construction = IntPtr.Zero;
            IntPtr pipeline = IntPtr.Zero;
            IntPtr runtime = IntPtr.Zero;
            IntPtr result = IntPtr.Zero;

            try
            {
                TerraKitNativeCall.ThrowIfFailed(
                    TerraKitNativeRegistryMethods.CreateBuiltInRegistry(out registry),
                    "tk_stage_registry_create_builtin");
                TerraKitNativeCall.ThrowIfFailed(
                    TerraKitNativePipelineMethods.CreateAssembler(registry, out assembler),
                    "tk_pipeline_assembler_create");

                var resourceKeys = new Dictionary<string, ulong>();
                ulong nextResourceKey = 1;
                var outputs = new List<TerraKitOutputDescriptor>();
                ulong stageId = 1;

                foreach (TerraKitNodeData node in compileResult.ExecutionOrder)
                {
                    TerraKitBackendStageSchema schema;
                    if (!schemas.TryGetValue(node.typeId, out schema))
                    {
                        throw new InvalidOperationException(
                            "The installed backend does not expose stage " + node.typeId + ".");
                    }

                    using (var typeId = new TerraKitNativeUtf8(node.typeId))
                    {
                        TerraKitNativeCall.ThrowIfFailed(
                            TerraKitNativePipelineMethods.CreateStage(
                                stageId++, typeId.View, node.schemaVersion, out construction),
                            "tk_stage_construction_create");
                    }

                    BindInputs(graph, node, schema, construction, resourceKeys);

                    foreach (TerraKitBackendOutputPort output in schema.Outputs)
                    {
                        ulong resourceKey = nextResourceKey++;
                        resourceKeys.Add(ResourceAddress(node.id, output.Id), resourceKey);
                        using (var portId = new TerraKitNativeUtf8(output.Id))
                        {
                            TerraKitNativeCall.ThrowIfFailed(
                                TerraKitNativePipelineMethods.BindOutput(
                                    construction, portId.View, resourceKey),
                                "tk_stage_construction_bind_output");
                        }

                        outputs.Add(new TerraKitOutputDescriptor(node.id, node.displayName,
                            output.Id, output.DisplayName, output.ResourceKind, resourceKey));
                    }

                    SetParameters(node, schema, construction);
                    TerraKitNativeCall.ThrowIfFailed(
                        TerraKitNativePipelineMethods.AddStage(assembler, ref construction),
                        "tk_pipeline_assembler_add_stage");
                }

                TerraKitNativeCall.ThrowIfFailed(
                    TerraKitNativePipelineMethods.FinishAssembler(ref assembler, out pipeline),
                    "tk_pipeline_assembler_finish");

                CreateRuntime(ref pipeline, generationRequest, out runtime);
                var generatedRegions = new List<TerraKitGeneratedRegion>(generationRequests.Count);
                foreach (TerraKitBackendGenerationRequest regionRequest in generationRequests)
                {
                    try
                    {
                        GenerateRegion(runtime, regionRequest, out result);
                        var resources = outputs.Select(output => TerraKitResourceReader.Read(result, output)).ToList();
                        generatedRegions.Add(new TerraKitGeneratedRegion(regionRequest, resources.AsReadOnly()));
                    }
                    finally
                    {
                        DestroyNoThrow(
                            "generation result",
                            ref result,
                            TerraKitNativeResultMethods.Destroy);
                    }
                    // Native generation is synchronous. Cancel only after its borrowed result is released.
                    if (shouldCancel != null && shouldCancel(generatedRegions.Count, generationRequests.Count))
                        throw new OperationCanceledException("Map generation cancelled.");
                }

                return generatedRegions.AsReadOnly();
            }
            finally
            {
                DestroyNoThrow("generation result", ref result, TerraKitNativeResultMethods.Destroy);
                DestroyNoThrow("runtime", ref runtime, TerraKitNativeRuntimeMethods.Destroy);
                DestroyNoThrow("pipeline", ref pipeline, TerraKitNativePipelineMethods.DestroyPipeline);
                DestroyNoThrow("stage construction", ref construction, TerraKitNativePipelineMethods.DestroyStage);
                DestroyNoThrow("pipeline assembler", ref assembler, TerraKitNativePipelineMethods.DestroyAssembler);
                DestroyNoThrow("stage registry", ref registry, TerraKitNativeRegistryMethods.DestroyRegistry);
            }
        }

        private static void BindInputs(
            TerraKitGraphAsset graph,
            TerraKitNodeData node,
            TerraKitBackendStageSchema schema,
            IntPtr construction,
            IDictionary<string, ulong> resourceKeys)
        {
            foreach (TerraKitBackendInputPort input in schema.Inputs)
            {
                TerraKitEdgeData edge = graph.edges.FirstOrDefault(
                    item => item.inputNodeId == node.id && item.inputPortId == input.Id);
                if (edge == null)
                {
                    if (input.IsOptional)
                    {
                        continue;
                    }

                    throw new InvalidOperationException(
                        node.DisplayLabel + " is missing backend input " + input.DisplayName + ".");
                }

                ulong resourceKey;
                if (!resourceKeys.TryGetValue(
                        ResourceAddress(edge.outputNodeId, edge.outputPortId),
                        out resourceKey))
                {
                    throw new InvalidOperationException(
                        "Could not resolve the resource connected to " +
                        node.DisplayLabel + "." + input.DisplayName + ".");
                }

                using (var portId = new TerraKitNativeUtf8(input.Id))
                {
                    TerraKitNativeCall.ThrowIfFailed(
                        TerraKitNativePipelineMethods.BindInput(
                            construction, portId.View, resourceKey),
                        "tk_stage_construction_bind_input");
                }
            }
        }

        private static void SetParameters(
            TerraKitNodeData node,
            TerraKitBackendStageSchema schema,
            IntPtr construction)
        {
            foreach (TerraKitBackendParameter parameterDefinition in schema.Parameters)
            {
                TerraKitParameterData parameterData = node.parameters.FirstOrDefault(
                    item => item.key == parameterDefinition.Id);
                string text = parameterData != null
                    ? parameterData.value
                    : parameterDefinition.DefaultValue == null
                        ? string.Empty
                        : parameterDefinition.DefaultValue.ToInvariantString();

                TerraKitNativeParameterValue value = ParseParameter(
                    node.DisplayLabel,
                    parameterDefinition,
                    text);
                using (var parameterId = new TerraKitNativeUtf8(parameterDefinition.Id))
                using (var textValue = NeedsText(value.Kind)
                    ? new TerraKitNativeUtf8(text)
                    : null)
                {
                    if (textValue != null)
                    {
                        value.TextValue = textValue.View;
                    }

                    TerraKitNativeCall.ThrowIfFailed(
                        TerraKitNativePipelineMethods.SetParameter(
                            construction, parameterId.View, ref value),
                        "tk_stage_construction_set_parameter");
                }
            }
        }

        private static TerraKitNativeParameterValue ParseParameter(
            string nodeName,
            TerraKitBackendParameter definition,
            string text)
        {
            var value = new TerraKitNativeParameterValue { Kind = (int)definition.Kind };
            string errorPrefix = nodeName + "." + definition.DisplayName + ": ";
            bool booleanValue;
            long signedValue;
            ulong unsignedValue;
            float float32Value;
            double float64Value;

            switch (definition.Kind)
            {
                case TerraKitBackendParameterKind.Boolean:
                    if (!bool.TryParse(text, out booleanValue))
                    {
                        throw new FormatException(errorPrefix + "expected true or false.");
                    }
                    value.BooleanValue = booleanValue ? (byte)1 : (byte)0;
                    break;
                case TerraKitBackendParameterKind.SignedInteger64:
                    if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out signedValue))
                    {
                        throw new FormatException(errorPrefix + "expected a whole number.");
                    }
                    value.SignedInteger64Value = signedValue;
                    break;
                case TerraKitBackendParameterKind.UnsignedInteger64:
                    if (!ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out unsignedValue))
                    {
                        throw new FormatException(errorPrefix + "expected a non-negative whole number.");
                    }
                    value.UnsignedInteger64Value = unsignedValue;
                    break;
                case TerraKitBackendParameterKind.Float32:
                    if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float32Value) ||
                        float.IsNaN(float32Value) || float.IsInfinity(float32Value))
                    {
                        throw new FormatException(errorPrefix + "expected a finite number.");
                    }
                    value.Float32Value = float32Value;
                    break;
                case TerraKitBackendParameterKind.Float64:
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float64Value) ||
                        double.IsNaN(float64Value) || double.IsInfinity(float64Value))
                    {
                        throw new FormatException(errorPrefix + "expected a finite number.");
                    }
                    value.Float64Value = float64Value;
                    break;
                case TerraKitBackendParameterKind.Vector2Float64:
                    double[] vector2 = ParseVector(text, 2, errorPrefix);
                    value.Vector2Float64Value = new TerraKitNativeVector2Float64
                    {
                        X = vector2[0], Y = vector2[1]
                    };
                    break;
                case TerraKitBackendParameterKind.Vector3Float64:
                    double[] vector3 = ParseVector(text, 3, errorPrefix);
                    value.Vector3Float64Value = new TerraKitNativeVector3Float64
                    {
                        X = vector3[0], Y = vector3[1], Z = vector3[2]
                    };
                    break;
                case TerraKitBackendParameterKind.String:
                case TerraKitBackendParameterKind.Enum:
                    break;
                default:
                    throw new NotSupportedException(
                        errorPrefix + "unsupported backend parameter kind " + definition.Kind + ".");
            }

            return value;
        }

        private static double[] ParseVector(string text, int componentCount, string errorPrefix)
        {
            string cleaned = (text ?? string.Empty).Trim().Trim('(', ')');
            string[] parts = cleaned.Split(',');
            if (parts.Length != componentCount)
            {
                throw new FormatException(
                    errorPrefix + "expected " + componentCount + " comma-separated numbers.");
            }

            var values = new double[componentCount];
            for (int index = 0; index < componentCount; index++)
            {
                if (!double.TryParse(
                        parts[index].Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out values[index]) ||
                    double.IsNaN(values[index]) ||
                    double.IsInfinity(values[index]))
                {
                    throw new FormatException(
                        errorPrefix + "expected " + componentCount + " comma-separated finite numbers.");
                }
            }

            return values;
        }

        internal static TerraKitGeneratedMeshData CopyMesh(TerraKitNativeTerrainMeshView view)
        {
            int positionCount = TerraKitNativeSize.CheckedToInt(view.PositionCount, "mesh position count");
            int indexCount = TerraKitNativeSize.CheckedToInt(view.IndexCount, "mesh index count");
            int normalCount = TerraKitNativeSize.CheckedToInt(view.NormalCount, "mesh normal count");
            int texcoordCount = TerraKitNativeSize.CheckedToInt(view.TexcoordCount, "mesh texcoord count");

            if (positionCount == 0 || view.Positions == IntPtr.Zero)
            {
                throw new InvalidOperationException("TerraKit returned an empty mesh position buffer.");
            }
            if (indexCount == 0 || view.Indices == IntPtr.Zero || indexCount % 3 != 0)
            {
                throw new InvalidOperationException("TerraKit returned an invalid triangle index buffer.");
            }
            if (normalCount != 0 && (normalCount != positionCount || view.Normals == IntPtr.Zero))
            {
                throw new InvalidOperationException("TerraKit returned a mismatched mesh normal buffer.");
            }
            if (texcoordCount != 0 && (texcoordCount != positionCount || view.Texcoords == IntPtr.Zero))
            {
                throw new InvalidOperationException("TerraKit returned a mismatched mesh texcoord buffer.");
            }

            Vector3[] positions = CopyVector3Array(view.Positions, positionCount);
            Vector3[] normals = normalCount == 0
                ? new Vector3[0]
                : CopyVector3Array(view.Normals, normalCount);
            Vector2[] texcoords = texcoordCount == 0
                ? new Vector2[0]
                : CopyVector2Array(view.Texcoords, texcoordCount);
            var indices = new int[indexCount];
            for (int index = 0; index < indexCount; index++)
            {
                int copied = Marshal.ReadInt32(view.Indices, index * sizeof(uint));
                if (copied < 0 || copied >= positionCount)
                {
                    throw new InvalidOperationException(
                        "TerraKit returned a mesh index outside the position buffer.");
                }
                indices[index] = copied;
            }

            return new TerraKitGeneratedMeshData(
                new TerraKitDouble3(view.Origin.X, view.Origin.Y, view.Origin.Z),
                positions,
                indices,
                normals,
                texcoords);
        }

        private static Vector3[] CopyVector3Array(IntPtr pointer, int count)
        {
            int stride = Marshal.SizeOf(typeof(TerraKitNativeVector3Float32));
            var result = new Vector3[count];
            for (int index = 0; index < count; index++)
            {
                var value = (TerraKitNativeVector3Float32)Marshal.PtrToStructure(
                    IntPtr.Add(pointer, index * stride),
                    typeof(TerraKitNativeVector3Float32));
                result[index] = new Vector3(value.X, value.Y, value.Z);
            }
            return result;
        }

        private static Vector2[] CopyVector2Array(IntPtr pointer, int count)
        {
            int stride = Marshal.SizeOf(typeof(TerraKitNativeVector2Float32));
            var result = new Vector2[count];
            for (int index = 0; index < count; index++)
            {
                var value = (TerraKitNativeVector2Float32)Marshal.PtrToStructure(
                    IntPtr.Add(pointer, index * stride),
                    typeof(TerraKitNativeVector2Float32));
                result[index] = new Vector2(value.X, value.Y);
            }
            return result;
        }

        private static bool NeedsText(int kind)
        {
            return kind == (int)TerraKitBackendParameterKind.String ||
                   kind == (int)TerraKitBackendParameterKind.Enum;
        }

        private static string ResourceAddress(string nodeId, string portId)
        {
            return nodeId + "\n" + portId;
        }

        private delegate int NativeDestroy(ref IntPtr handle);

        private static void DestroyNoThrow(string name, ref IntPtr handle, NativeDestroy destroy)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            int status = destroy(ref handle);
            if (status != TerraKitNativeStatusCode.Ok)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[TerraKit] Failed to destroy native " + name + ". Status " + status + ".");
            }
        }
    }

    internal sealed class TerraKitNativeUtf8 : IDisposable
    {
        internal TerraKitNativeStringView View { get; private set; }

        internal TerraKitNativeUtf8(string text)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty);
            IntPtr data = bytes.Length == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(bytes.Length);
            if (bytes.Length > 0)
            {
                Marshal.Copy(bytes, 0, data, bytes.Length);
            }
            View = new TerraKitNativeStringView { Data = data, Length = new UIntPtr((uint)bytes.Length) };
        }

        public void Dispose()
        {
            if (View.Data != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(View.Data);
                View = default(TerraKitNativeStringView);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerraKitNativeVector3Float32
    {
        public float X;
        public float Y;
        public float Z;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerraKitNativeVector2Float32
    {
        public float X;
        public float Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerraKitNativeTerrainMeshView
    {
        public TerraKitNativeVector3Float64 Origin;
        public IntPtr Positions;
        public UIntPtr PositionCount;
        public IntPtr Indices;
        public UIntPtr IndexCount;
        public IntPtr Normals;
        public UIntPtr NormalCount;
        public IntPtr Texcoords;
        public UIntPtr TexcoordCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerraKitNativeRegionLayout2D
    {
        public uint CellWidth;
        public uint CellHeight;
        public TerraKitNativeVector2Float64 BaseSpacing;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerraKitNativeGenerationRequest2D
    {
        public ulong Seed;
        public long RegionX;
        public long RegionY;
        public ushort LodLevel;
        public byte Reserved0;
        public byte Reserved1;
        public byte Reserved2;
        public byte Reserved3;
        public byte Reserved4;
        public byte Reserved5;
    }

    internal static class TerraKitBackendExecutionAbiLayout
    {
        internal static void Validate()
        {
            RequireSize<TerraKitNativeVector3Float32>(12);
            RequireSize<TerraKitNativeVector2Float32>(8);
            RequireSize<TerraKitNativeTerrainMeshView>(88);
            RequireSize<TerraKitNativeRegionLayout2D>(24);
            RequireSize<TerraKitNativeGenerationRequest2D>(32);
            RequireSize<TerraKitNativeRegionLayout3D>(40);
            RequireSize<TerraKitNativeGenerationRequest3D>(40);
            RequireSize<TerraKitNativeHeightFieldView>(128);
            RequireSize<TerraKitNativeVolumeView>(136);
        }

        private static void RequireSize<T>(int expected)
        {
            int actual = Marshal.SizeOf(typeof(T));
            if (actual != expected)
            {
                throw new InvalidOperationException(
                    "TerraKit ABI layout mismatch for " + typeof(T).Name +
                    ": expected " + expected + " bytes, got " + actual + ".");
            }
        }
    }

    internal static class TerraKitNativePipelineMethods
    {
        private const string LibraryName = "terrakit";

        [DllImport(LibraryName, EntryPoint = "tk_stage_construction_create", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int CreateStage(ulong stageId, TerraKitNativeStringView typeId, uint schemaVersion, out IntPtr construction);

        [DllImport(LibraryName, EntryPoint = "tk_stage_construction_destroy", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int DestroyStage(ref IntPtr construction);

        [DllImport(LibraryName, EntryPoint = "tk_stage_construction_set_parameter", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int SetParameter(IntPtr construction, TerraKitNativeStringView parameterId, ref TerraKitNativeParameterValue value);

        [DllImport(LibraryName, EntryPoint = "tk_stage_construction_bind_input", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int BindInput(IntPtr construction, TerraKitNativeStringView portId, ulong resourceKey);

        [DllImport(LibraryName, EntryPoint = "tk_stage_construction_bind_output", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int BindOutput(IntPtr construction, TerraKitNativeStringView portId, ulong resourceKey);

        [DllImport(LibraryName, EntryPoint = "tk_pipeline_assembler_create", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int CreateAssembler(IntPtr registry, out IntPtr assembler);

        [DllImport(LibraryName, EntryPoint = "tk_pipeline_assembler_destroy", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int DestroyAssembler(ref IntPtr assembler);

        [DllImport(LibraryName, EntryPoint = "tk_pipeline_assembler_add_stage", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int AddStage(IntPtr assembler, ref IntPtr construction);

        [DllImport(LibraryName, EntryPoint = "tk_pipeline_assembler_finish", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int FinishAssembler(ref IntPtr assembler, out IntPtr pipeline);

        [DllImport(LibraryName, EntryPoint = "tk_pipeline_destroy", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int DestroyPipeline(ref IntPtr pipeline);
    }

    internal static partial class TerraKitNativeRuntimeMethods
    {
        private const string LibraryName = "terrakit";

        [DllImport(LibraryName, EntryPoint = "tk_runtime_create_2d", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int Create2D(ref IntPtr pipeline, ref TerraKitNativeRegionLayout2D layout, out IntPtr runtime);

        [DllImport(LibraryName, EntryPoint = "tk_runtime_generate_2d", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int Generate2D(IntPtr runtime, ref TerraKitNativeGenerationRequest2D request, out IntPtr result);

        [DllImport(LibraryName, EntryPoint = "tk_runtime_destroy", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int Destroy(ref IntPtr runtime);
    }

    internal static partial class TerraKitNativeResultMethods
    {
        private const string LibraryName = "terrakit";

        [DllImport(LibraryName, EntryPoint = "tk_generation_result_get_mesh", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int GetMesh(IntPtr result, ulong resourceKey, out TerraKitNativeTerrainMeshView view);

        [DllImport(LibraryName, EntryPoint = "tk_generation_result_destroy", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int Destroy(ref IntPtr result);
    }
}
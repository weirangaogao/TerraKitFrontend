using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace TerraKit
{
    public enum TerraKitBackendResourceKind
    {
        Invalid = 0,
        HeightField = 1,
        DensityField = 2,
        VoxelVolume = 3,
        Mesh = 4
    }

    public enum TerraKitBackendParameterKind
    {
        Invalid = 0,
        Boolean = 1,
        SignedInteger64 = 2,
        UnsignedInteger64 = 3,
        Float32 = 4,
        Float64 = 5,
        Vector2Float64 = 6,
        Vector3Float64 = 7,
        String = 8,
        Enum = 9
    }

    public sealed class TerraKitBackendInputPort
    {
        public string Id { get; private set; }
        public string DisplayName { get; private set; }
        public string Description { get; private set; }
        public TerraKitBackendResourceKind ResourceKind { get; private set; }
        public bool IsOptional { get; private set; }

        internal TerraKitBackendInputPort(TerraKitNativeInputPortInfo native)
        {
            Id = native.Id.CopyUtf8();
            DisplayName = native.DisplayName.CopyUtf8();
            Description = native.Description.CopyUtf8();
            ResourceKind = (TerraKitBackendResourceKind)native.ResourceKind;
            IsOptional = native.Optional != 0;
        }
    }

    public sealed class TerraKitBackendOutputPort
    {
        public string Id { get; private set; }
        public string DisplayName { get; private set; }
        public string Description { get; private set; }
        public TerraKitBackendResourceKind ResourceKind { get; private set; }

        internal TerraKitBackendOutputPort(TerraKitNativeOutputPortInfo native)
        {
            Id = native.Id.CopyUtf8();
            DisplayName = native.DisplayName.CopyUtf8();
            Description = native.Description.CopyUtf8();
            ResourceKind = (TerraKitBackendResourceKind)native.ResourceKind;
        }
    }

    public sealed class TerraKitBackendEnumOption
    {
        public string Id { get; private set; }
        public string DisplayName { get; private set; }
        public string Description { get; private set; }

        internal TerraKitBackendEnumOption(TerraKitNativeEnumOptionInfo native)
        {
            Id = native.Id.CopyUtf8();
            DisplayName = native.DisplayName.CopyUtf8();
            Description = native.Description.CopyUtf8();
        }
    }

    public sealed class TerraKitBackendParameterValue
    {
        public TerraKitBackendParameterKind Kind { get; private set; }
        public bool BooleanValue { get; private set; }
        public long SignedInteger64Value { get; private set; }
        public ulong UnsignedInteger64Value { get; private set; }
        public float Float32Value { get; private set; }
        public double Float64Value { get; private set; }
        public double VectorX { get; private set; }
        public double VectorY { get; private set; }
        public double VectorZ { get; private set; }
        public string TextValue { get; private set; }

        internal TerraKitBackendParameterValue(TerraKitNativeParameterValue native)
        {
            Kind = (TerraKitBackendParameterKind)native.Kind;
            BooleanValue = native.BooleanValue != 0;
            SignedInteger64Value = native.SignedInteger64Value;
            UnsignedInteger64Value = native.UnsignedInteger64Value;
            Float32Value = native.Float32Value;
            Float64Value = native.Float64Value;
            VectorX = native.Vector2Float64Value.X;
            VectorY = native.Vector2Float64Value.Y;
            VectorZ = 0;

            if (Kind == TerraKitBackendParameterKind.Vector3Float64)
            {
                VectorX = native.Vector3Float64Value.X;
                VectorY = native.Vector3Float64Value.Y;
                VectorZ = native.Vector3Float64Value.Z;
            }

            TextValue =
                Kind == TerraKitBackendParameterKind.String ||
                Kind == TerraKitBackendParameterKind.Enum
                    ? native.TextValue.CopyUtf8()
                    : string.Empty;
        }

        public string ToInvariantString()
        {
            switch (Kind)
            {
                case TerraKitBackendParameterKind.Boolean:
                    return BooleanValue ? "true" : "false";
                case TerraKitBackendParameterKind.SignedInteger64:
                    return SignedInteger64Value.ToString(CultureInfo.InvariantCulture);
                case TerraKitBackendParameterKind.UnsignedInteger64:
                    return UnsignedInteger64Value.ToString(CultureInfo.InvariantCulture);
                case TerraKitBackendParameterKind.Float32:
                    return Float32Value.ToString("R", CultureInfo.InvariantCulture);
                case TerraKitBackendParameterKind.Float64:
                    return Float64Value.ToString("R", CultureInfo.InvariantCulture);
                case TerraKitBackendParameterKind.Vector2Float64:
                    return VectorX.ToString("R", CultureInfo.InvariantCulture) + ", " +
                           VectorY.ToString("R", CultureInfo.InvariantCulture);
                case TerraKitBackendParameterKind.Vector3Float64:
                    return VectorX.ToString("R", CultureInfo.InvariantCulture) + ", " +
                           VectorY.ToString("R", CultureInfo.InvariantCulture) + ", " +
                           VectorZ.ToString("R", CultureInfo.InvariantCulture);
                case TerraKitBackendParameterKind.String:
                case TerraKitBackendParameterKind.Enum:
                    return TextValue;
                default:
                    return string.Empty;
            }
        }
    }

    public sealed class TerraKitBackendParameter
    {
        public string Id { get; private set; }
        public string DisplayName { get; private set; }
        public string Description { get; private set; }
        public TerraKitBackendParameterKind Kind { get; private set; }
        public TerraKitBackendParameterValue DefaultValue { get; private set; }
        public TerraKitBackendParameterValue MinimumValue { get; private set; }
        public TerraKitBackendParameterValue MaximumValue { get; private set; }
        public IReadOnlyList<TerraKitBackendEnumOption> EnumOptions { get; private set; }

        internal TerraKitBackendParameter(
            TerraKitNativeParameterInfo native,
            IReadOnlyList<TerraKitBackendEnumOption> enumOptions)
        {
            Id = native.Id.CopyUtf8();
            DisplayName = native.DisplayName.CopyUtf8();
            Description = native.Description.CopyUtf8();
            Kind = (TerraKitBackendParameterKind)native.Kind;
            DefaultValue = native.HasDefault != 0
                ? new TerraKitBackendParameterValue(native.DefaultValue)
                : null;
            MinimumValue = native.HasMinimum != 0
                ? new TerraKitBackendParameterValue(native.MinimumValue)
                : null;
            MaximumValue = native.HasMaximum != 0
                ? new TerraKitBackendParameterValue(native.MaximumValue)
                : null;
            EnumOptions = enumOptions;
        }
    }

    /// <summary>
    /// Read-only metadata copied from the native built-in stage registry.
    /// All borrowed native strings are copied before the registry is destroyed.
    /// </summary>
    public sealed class TerraKitBackendStageSchema
    {
        public string TypeId { get; private set; }
        public uint SchemaVersion { get; private set; }
        public string DisplayName { get; private set; }
        public string Category { get; private set; }
        public string Description { get; private set; }
        public IReadOnlyList<TerraKitBackendInputPort> Inputs { get; private set; }
        public IReadOnlyList<TerraKitBackendOutputPort> Outputs { get; private set; }
        public IReadOnlyList<TerraKitBackendParameter> Parameters { get; private set; }

        public int InputCount { get { return Inputs.Count; } }
        public int OutputCount { get { return Outputs.Count; } }
        public int ParameterCount { get { return Parameters.Count; } }

        internal TerraKitBackendStageSchema(
            TerraKitNativeStageSchemaInfo native,
            IReadOnlyList<TerraKitBackendInputPort> inputs,
            IReadOnlyList<TerraKitBackendOutputPort> outputs,
            IReadOnlyList<TerraKitBackendParameter> parameters)
        {
            TypeId = native.TypeId.CopyUtf8();
            SchemaVersion = native.SchemaVersion;
            DisplayName = native.DisplayName.CopyUtf8();
            Category = native.Category.CopyUtf8();
            Description = native.Description.CopyUtf8();
            Inputs = inputs;
            Outputs = outputs;
            Parameters = parameters;
        }
    }

    // Reads stage information from the Rust backend,
    // including inputs, outputs, and parameters.

    public static class TerraKitBackendSchemaDiscovery
    {
        public static IReadOnlyList<TerraKitBackendStageSchema> DiscoverBuiltInStages()
        {
            TerraKitNativeStatus availability = TerraKitNativeLibrary.CheckAvailability();
            if (!availability.IsAvailable)
            {
                throw new TerraKitNativeException(
                    availability.StatusCode,
                    availability.Summary + " " + availability.Details);
            }

            TerraKitNativeAbiLayout.Validate();
            IntPtr registry = IntPtr.Zero;
            try
            {
                TerraKitNativeCall.ThrowIfFailed(
                    TerraKitNativeRegistryMethods.CreateBuiltInRegistry(out registry),
                    "tk_stage_registry_create_builtin");

                UIntPtr nativeCount;
                TerraKitNativeCall.ThrowIfFailed(
                    TerraKitNativeRegistryMethods.GetSchemaCount(registry, out nativeCount),
                    "tk_stage_registry_get_schema_count");

                int count = TerraKitNativeSize.CheckedToInt(nativeCount, "schema count");
                var schemas = new List<TerraKitBackendStageSchema>(count);
                for (int schemaIndex = 0; schemaIndex < count; schemaIndex++)
                {
                    schemas.Add(ReadSchema(registry, schemaIndex));
                }

                return schemas.AsReadOnly();
            }
            finally
            {
                if (registry != IntPtr.Zero)
                {
                    int destroyStatus = TerraKitNativeRegistryMethods.DestroyRegistry(ref registry);
                    if (destroyStatus != TerraKitNativeStatusCode.Ok)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "[TerraKit] Failed to destroy the native stage registry. Status " +
                            destroyStatus + ": " + TerraKitNativeMethods.CopyLastError());
                    }
                }
            }
        }

        private static TerraKitBackendStageSchema ReadSchema(IntPtr registry, int schemaIndex)
        {
            UIntPtr nativeSchemaIndex = new UIntPtr((uint)schemaIndex);
            TerraKitNativeStageSchemaInfo nativeSchema;
            TerraKitNativeCall.ThrowIfFailed(
                TerraKitNativeRegistryMethods.GetSchema(
                    registry,
                    nativeSchemaIndex,
                    out nativeSchema),
                "tk_stage_registry_get_schema");

            int inputCount = TerraKitNativeSize.CheckedToInt(nativeSchema.InputCount, "input count");
            int outputCount = TerraKitNativeSize.CheckedToInt(nativeSchema.OutputCount, "output count");
            int parameterCount = TerraKitNativeSize.CheckedToInt(
                nativeSchema.ParameterCount,
                "parameter count");

            var inputs = new List<TerraKitBackendInputPort>(inputCount);
            for (int inputIndex = 0; inputIndex < inputCount; inputIndex++)
            {
                TerraKitNativeInputPortInfo nativeInput;
                TerraKitNativeCall.ThrowIfFailed(
                    TerraKitNativeRegistryMethods.GetInputPort(
                        registry,
                        nativeSchemaIndex,
                        new UIntPtr((uint)inputIndex),
                        out nativeInput),
                    "tk_stage_registry_get_input_port");
                inputs.Add(new TerraKitBackendInputPort(nativeInput));
            }

            var outputs = new List<TerraKitBackendOutputPort>(outputCount);
            for (int outputIndex = 0; outputIndex < outputCount; outputIndex++)
            {
                TerraKitNativeOutputPortInfo nativeOutput;
                TerraKitNativeCall.ThrowIfFailed(
                    TerraKitNativeRegistryMethods.GetOutputPort(
                        registry,
                        nativeSchemaIndex,
                        new UIntPtr((uint)outputIndex),
                        out nativeOutput),
                    "tk_stage_registry_get_output_port");
                outputs.Add(new TerraKitBackendOutputPort(nativeOutput));
            }

            var parameters = new List<TerraKitBackendParameter>(parameterCount);
            for (int parameterIndex = 0; parameterIndex < parameterCount; parameterIndex++)
            {
                parameters.Add(ReadParameter(
                    registry,
                    nativeSchemaIndex,
                    parameterIndex));
            }

            return new TerraKitBackendStageSchema(
                nativeSchema,
                inputs.AsReadOnly(),
                outputs.AsReadOnly(),
                parameters.AsReadOnly());
        }

        private static TerraKitBackendParameter ReadParameter(
            IntPtr registry,
            UIntPtr schemaIndex,
            int parameterIndex)
        {
            UIntPtr nativeParameterIndex = new UIntPtr((uint)parameterIndex);
            TerraKitNativeParameterInfo nativeParameter;
            TerraKitNativeCall.ThrowIfFailed(
                TerraKitNativeRegistryMethods.GetParameter(
                    registry,
                    schemaIndex,
                    nativeParameterIndex,
                    out nativeParameter),
                "tk_stage_registry_get_parameter");

            int enumOptionCount = TerraKitNativeSize.CheckedToInt(
                nativeParameter.EnumOptionCount,
                "enum option count");
            var enumOptions = new List<TerraKitBackendEnumOption>(enumOptionCount);
            for (int optionIndex = 0; optionIndex < enumOptionCount; optionIndex++)
            {
                TerraKitNativeEnumOptionInfo nativeOption;
                TerraKitNativeCall.ThrowIfFailed(
                    TerraKitNativeRegistryMethods.GetEnumOption(
                        registry,
                        schemaIndex,
                        nativeParameterIndex,
                        new UIntPtr((uint)optionIndex),
                        out nativeOption),
                    "tk_stage_registry_get_enum_option");
                enumOptions.Add(new TerraKitBackendEnumOption(nativeOption));
            }

            return new TerraKitBackendParameter(nativeParameter, enumOptions.AsReadOnly());
        }
    }

    public sealed class TerraKitNativeException : Exception
    {
        public int StatusCode { get; private set; }

        internal TerraKitNativeException(int statusCode, string message)
            : base(message)
        {
            StatusCode = statusCode;
        }
    }

    internal static class TerraKitNativeCall
    {
        internal static void ThrowIfFailed(int status, string operation)
        {
            if (status == TerraKitNativeStatusCode.Ok)
            {
                return;
            }

            string details = TerraKitNativeMethods.CopyLastError();
            throw new TerraKitNativeException(
                status,
                operation + " failed with status " + status +
                (string.IsNullOrEmpty(details) ? "." : ": " + details));
        }
    }

    internal static class TerraKitNativeSize
    {
        internal static int CheckedToInt(UIntPtr value, string fieldName)
        {
            ulong converted = value.ToUInt64();
            if (converted > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "TerraKit returned a " + fieldName + " larger than Unity can represent.");
            }

            return (int)converted;
        }
    }

    internal static class TerraKitNativeAbiLayout
    {
        internal static void Validate()
        {
            if (IntPtr.Size != 8)
            {
                throw new PlatformNotSupportedException(
                    "The TerraKit Unity integration requires a 64-bit Unity process.");
            }

            RequireSize<TerraKitNativeStringView>(16);
            RequireSize<TerraKitNativeParameterValue>(96);
            RequireSize<TerraKitNativeStageSchemaInfo>(96);
            RequireSize<TerraKitNativeInputPortInfo>(56);
            RequireSize<TerraKitNativeOutputPortInfo>(56);
            RequireSize<TerraKitNativeParameterInfo>(368);
            RequireSize<TerraKitNativeEnumOptionInfo>(48);
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

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerraKitNativeVector2Float64
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerraKitNativeVector3Float64
    {
        public double X;
        public double Y;
        public double Z;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerraKitNativeParameterValue
    {
        public int Kind;
        public byte BooleanValue;
        public byte ReservedBoolean0;
        public byte ReservedBoolean1;
        public byte ReservedBoolean2;
        public long SignedInteger64Value;
        public ulong UnsignedInteger64Value;
        public float Float32Value;
        public uint ReservedFloat32;
        public double Float64Value;
        public TerraKitNativeVector2Float64 Vector2Float64Value;
        public TerraKitNativeVector3Float64 Vector3Float64Value;
        public TerraKitNativeStringView TextValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerraKitNativeStageSchemaInfo
    {
        public TerraKitNativeStringView TypeId;
        public uint SchemaVersion;
        public TerraKitNativeStringView DisplayName;
        public TerraKitNativeStringView Category;
        public TerraKitNativeStringView Description;
        public UIntPtr InputCount;
        public UIntPtr OutputCount;
        public UIntPtr ParameterCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerraKitNativeInputPortInfo
    {
        public TerraKitNativeStringView Id;
        public TerraKitNativeStringView DisplayName;
        public TerraKitNativeStringView Description;
        public int ResourceKind;
        public byte Optional;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerraKitNativeOutputPortInfo
    {
        public TerraKitNativeStringView Id;
        public TerraKitNativeStringView DisplayName;
        public TerraKitNativeStringView Description;
        public int ResourceKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerraKitNativeParameterInfo
    {
        public TerraKitNativeStringView Id;
        public TerraKitNativeStringView DisplayName;
        public TerraKitNativeStringView Description;
        public int Kind;
        public byte HasDefault;
        public TerraKitNativeParameterValue DefaultValue;
        public byte HasMinimum;
        public TerraKitNativeParameterValue MinimumValue;
        public byte HasMaximum;
        public TerraKitNativeParameterValue MaximumValue;
        public UIntPtr EnumOptionCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerraKitNativeEnumOptionInfo
    {
        public TerraKitNativeStringView Id;
        public TerraKitNativeStringView DisplayName;
        public TerraKitNativeStringView Description;
    }

    internal static class TerraKitNativeRegistryMethods
    {
        private const string LibraryName = "terrakit";

        [DllImport(
            LibraryName,
            EntryPoint = "tk_stage_registry_create_builtin",
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true)]
        internal static extern int CreateBuiltInRegistry(out IntPtr registry);

        [DllImport(
            LibraryName,
            EntryPoint = "tk_stage_registry_destroy",
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true)]
        internal static extern int DestroyRegistry(ref IntPtr registry);

        [DllImport(
            LibraryName,
            EntryPoint = "tk_stage_registry_get_schema_count",
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true)]
        internal static extern int GetSchemaCount(IntPtr registry, out UIntPtr count);

        [DllImport(
            LibraryName,
            EntryPoint = "tk_stage_registry_get_schema",
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true)]
        internal static extern int GetSchema(
            IntPtr registry,
            UIntPtr schemaIndex,
            out TerraKitNativeStageSchemaInfo schema);

        [DllImport(
            LibraryName,
            EntryPoint = "tk_stage_registry_get_input_port",
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true)]
        internal static extern int GetInputPort(
            IntPtr registry,
            UIntPtr schemaIndex,
            UIntPtr inputIndex,
            out TerraKitNativeInputPortInfo port);

        [DllImport(
            LibraryName,
            EntryPoint = "tk_stage_registry_get_output_port",
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true)]
        internal static extern int GetOutputPort(
            IntPtr registry,
            UIntPtr schemaIndex,
            UIntPtr outputIndex,
            out TerraKitNativeOutputPortInfo port);

        [DllImport(
            LibraryName,
            EntryPoint = "tk_stage_registry_get_parameter",
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true)]
        internal static extern int GetParameter(
            IntPtr registry,
            UIntPtr schemaIndex,
            UIntPtr parameterIndex,
            out TerraKitNativeParameterInfo parameter);

        [DllImport(
            LibraryName,
            EntryPoint = "tk_stage_registry_get_enum_option",
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true)]
        internal static extern int GetEnumOption(
            IntPtr registry,
            UIntPtr schemaIndex,
            UIntPtr parameterIndex,
            UIntPtr optionIndex,
            out TerraKitNativeEnumOptionInfo option);
    }
}
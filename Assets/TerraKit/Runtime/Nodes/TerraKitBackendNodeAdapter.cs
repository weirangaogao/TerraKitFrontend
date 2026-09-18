using System;
using System.Linq;

namespace TerraKit
{
    public static class TerraKitBackendNodeAdapter
    {
        public static ITerraKitNodeDefinition Create(
            TerraKitBackendStageSchema schema)
        {
            try
            {
                if (schema.Inputs.Select(port => port.Id).Distinct().Count()
                        != schema.Inputs.Count ||
                    schema.Outputs.Select(port => port.Id).Distinct().Count()
                        != schema.Outputs.Count ||
                    schema.Parameters.Select(parameter => parameter.Id).Distinct().Count()
                        != schema.Parameters.Count)
                {
                    throw new InvalidOperationException(
                        "Duplicate port or parameter ids.");
                }

                var inputs = schema.Inputs.Select(port =>
                    new TerraKitPortDefinition(
                        port.Id,
                        port.DisplayName,
                        MapResource(port.ResourceKind),
                        !port.IsOptional,
                        false))
                    .ToList()
                    .AsReadOnly();

                var outputs = schema.Outputs.Select(port =>
                    new TerraKitPortDefinition(
                        port.Id,
                        port.DisplayName,
                        MapResource(port.ResourceKind),
                        false,
                        true))
                    .ToList()
                    .AsReadOnly();

                var parameters = schema.Parameters.Select(parameter => new TerraKitParameterDefinition(
                        parameter.Id,
                        parameter.DisplayName,
                        MapParameter(parameter.Kind),
                        parameter.DefaultValue != null
                            ? parameter.DefaultValue.ToInvariantString()
                            : string.Empty,
                        description: parameter.Description,
                        backendParameter: parameter))
                    .ToList()
                    .AsReadOnly();

                return new TerraKitNodeDefinition(
                    schema.TypeId,
                    schema.DisplayName,
                    schema.Category,
                    inputs,
                    outputs,
                    parameters,
                    schema.Description,
                    schema.SchemaVersion);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Cannot display backend stage '" +
                    schema.TypeId + "': " + exception.Message,
                    exception);
            }
        }

        private static TerraKitPortType MapResource(
            TerraKitBackendResourceKind kind)
        {
            switch (kind)
            {
                case TerraKitBackendResourceKind.HeightField:
                    return TerraKitPortType.HeightMap;

                case TerraKitBackendResourceKind.Mesh:
                    return TerraKitPortType.Mesh;

                case TerraKitBackendResourceKind.DensityField:
                    return TerraKitPortType.DensityField;

                case TerraKitBackendResourceKind.VoxelVolume:
                    return TerraKitPortType.VoxelVolume;

                default:
                    throw new NotSupportedException(
                        "Unsupported resource type: " + kind);
            }
        }

        private static TerraKitParameterType MapParameter(
            TerraKitBackendParameterKind kind)
        {
            switch (kind)
            {
                case TerraKitBackendParameterKind.Boolean:
                    return TerraKitParameterType.Boolean;

                case TerraKitBackendParameterKind.SignedInteger64:
                case TerraKitBackendParameterKind.UnsignedInteger64:
                    return TerraKitParameterType.Integer;

                case TerraKitBackendParameterKind.Float32:
                case TerraKitBackendParameterKind.Float64:
                    return TerraKitParameterType.Float;

                case TerraKitBackendParameterKind.Vector2Float64:
                case TerraKitBackendParameterKind.Vector3Float64:
                case TerraKitBackendParameterKind.String:
                case TerraKitBackendParameterKind.Enum:
                    return TerraKitParameterType.String;

                default:
                    throw new NotSupportedException(
                        "Unsupported parameter type: " + kind);
            }
        }
    }
}
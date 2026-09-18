using System;
using System.Linq;

namespace TerraKit
{
    public static class TerraKitBackendParameterRules
    {
        public static string BuildHint(TerraKitBackendParameter schema)
        {
            switch (schema.Kind)
            {
                case TerraKitBackendParameterKind.SignedInteger64:
                case TerraKitBackendParameterKind.UnsignedInteger64:
                case TerraKitBackendParameterKind.Float32:
                case TerraKitBackendParameterKind.Float64:
                    return BuildNumericHint(schema);

                case TerraKitBackendParameterKind.Enum:
                    return string.Join(", ", schema.EnumOptions.Select(option =>
                        string.IsNullOrEmpty(option.DisplayName)
                            ? option.Id
                            : option.DisplayName));

                case TerraKitBackendParameterKind.Boolean:
                    return "true or false";

                case TerraKitBackendParameterKind.Vector2Float64:
                    return "2 comma-separated finite numbers, e.g. (0, 1)";

                case TerraKitBackendParameterKind.Vector3Float64:
                    return "3 comma-separated finite numbers, e.g. (0, 1, 0)";

                case TerraKitBackendParameterKind.String:
                    return "Text";

                default:
                    throw new NotSupportedException(
                        "Unsupported backend parameter type: " + schema.Kind);
            }
        }

        public static string NumericTypeName(TerraKitBackendParameterKind kind)
        {
            switch (kind)
            {
                case TerraKitBackendParameterKind.SignedInteger64:
                    return "signed 64-bit integer";
                case TerraKitBackendParameterKind.UnsignedInteger64:
                    return "unsigned 64-bit integer";
                case TerraKitBackendParameterKind.Float32:
                    return "finite 32-bit number";
                case TerraKitBackendParameterKind.Float64:
                    return "finite 64-bit number";
                default:
                    throw new ArgumentException("Expected a numeric backend type.");
            }
        }

        private static string BuildNumericHint(TerraKitBackendParameter schema)
        {
            string hint = NumericTypeName(schema.Kind);
            string minimum = schema.MinimumValue != null
                ? schema.MinimumValue.ToInvariantString()
                : schema.Kind == TerraKitBackendParameterKind.UnsignedInteger64
                    ? "0"
                    : null;

            if (minimum != null)
            {
                hint += " at least " + minimum;
            }

            if (schema.MaximumValue != null)
            {
                hint += (minimum != null ? " and at most " : " at most ") +
                        schema.MaximumValue.ToInvariantString();
            }

            return hint;
        }
    }
}
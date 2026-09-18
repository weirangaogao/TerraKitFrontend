using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TerraKit
{
    public sealed class TerraKitParameterValidationIssue
    {
        public string ParameterKey { get; private set; }
        public string Message { get; private set; }

        public TerraKitParameterValidationIssue(string parameterKey, string message)
        {
            ParameterKey = parameterKey;
            Message = message;
        }
    }

    public static class TerraKitParameterValidator
    {
        public static IReadOnlyList<TerraKitParameterValidationIssue> Validate(
            TerraKitParameterDefinition definition,
            TerraKitParameterData parameter)
        {
            var issues = new List<TerraKitParameterValidationIssue>();

            if (definition == null)
            {
                issues.Add(new TerraKitParameterValidationIssue(
                    parameter != null ? parameter.key : string.Empty,
                    "Parameter definition is missing."));
                return issues;
            }

            if (parameter == null)
            {
                issues.Add(new TerraKitParameterValidationIssue(
                    definition.Key,
                    "Parameter value is missing."));
                return issues;
            }

            return ValidateBackend(definition.BackendParameter, parameter);
        }

        private static IReadOnlyList<TerraKitParameterValidationIssue> ValidateBackend(
            TerraKitBackendParameter schema,
            TerraKitParameterData parameter)
        {
            var issues = new List<TerraKitParameterValidationIssue>();
            string text = parameter.value;
            string message = null;

            switch (schema.Kind)
            {
                case TerraKitBackendParameterKind.SignedInteger64:
                case TerraKitBackendParameterKind.UnsignedInteger64:
                case TerraKitBackendParameterKind.Float32:
                case TerraKitBackendParameterKind.Float64:
                    return ValidateBackendNumeric(schema, parameter);

                case TerraKitBackendParameterKind.Enum:
                    bool matches = schema.EnumOptions.Any(option =>
                        string.Equals(option.Id, text, StringComparison.Ordinal));

                    if (!matches)
                    {
                        message = "Value must be one of: " +
                            string.Join(", ", schema.EnumOptions.Select(option => option.Id)) +
                            ". Current value: " + FormatCurrentValue(text) + ".";
                    }
                    break;

                case TerraKitBackendParameterKind.Boolean:
                    bool booleanValue;

                    if (!bool.TryParse(text, out booleanValue))
                    {
                        message = "Expected true or false. Current value: " +
                            FormatCurrentValue(text) + ".";
                    }
                    break;

                case TerraKitBackendParameterKind.Vector2Float64:
                case TerraKitBackendParameterKind.Vector3Float64:
                    int count = schema.Kind == TerraKitBackendParameterKind.Vector2Float64 ? 2 : 3;

                    if (!IsValidBackendVector(text, count))
                    {
                        message = "Expected " + count +
                            " comma-separated finite numbers. Current value: " +
                            FormatCurrentValue(text) + ".";
                    }
                    break;

                case TerraKitBackendParameterKind.String:
                    break;

                default:
                    message = "Unsupported backend parameter type: " + schema.Kind + ".";
                    break;
            }

            if (message != null)
            {
                issues.Add(new TerraKitParameterValidationIssue(
                    parameter.key,
                    message));
            }

            return issues;
        }

        private static bool IsValidBackendVector(string text, int componentCount)
        {
            string cleaned = (text ?? string.Empty).Trim().Trim('(', ')');
            string[] parts = cleaned.Split(',');

            if (parts.Length != componentCount)
            {
                return false;
            }

            foreach (string part in parts)
            {
                double value;

                if (!double.TryParse(
                        part.Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out value) ||
                    double.IsNaN(value) ||
                    double.IsInfinity(value))
                {
                    return false;
                }
            }

            return true;
        }

        private static IReadOnlyList<TerraKitParameterValidationIssue> ValidateBackendNumeric(
            TerraKitBackendParameter schema,
            TerraKitParameterData parameter)
        {
            var issues = new List<TerraKitParameterValidationIssue>();
            string text = parameter.value;

            if ((schema.MinimumValue != null && schema.MinimumValue.Kind != schema.Kind) ||
                (schema.MaximumValue != null && schema.MaximumValue.Kind != schema.Kind))
            {
                issues.Add(new TerraKitParameterValidationIssue(
                    parameter.key, "Backend numeric bounds have an incompatible type."));
                return issues;
            }

            switch (schema.Kind)
            {
                case TerraKitBackendParameterKind.SignedInteger64:
                    long signedValue;
                    if (long.TryParse(text, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out signedValue))
                    {
                        CheckBackendBounds(schema, parameter, signedValue,
                            bound => bound.SignedInteger64Value, issues);
                        return issues;
                    }
                    break;

                case TerraKitBackendParameterKind.UnsignedInteger64:
                    ulong unsignedValue;
                    if (ulong.TryParse(text, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out unsignedValue))
                    {
                        CheckBackendBounds(schema, parameter, unsignedValue,
                            bound => bound.UnsignedInteger64Value, issues);
                        return issues;
                    }
                    break;

                case TerraKitBackendParameterKind.Float32:
                    float singleValue;
                    if (float.TryParse(text, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out singleValue) &&
                        !float.IsNaN(singleValue) && !float.IsInfinity(singleValue))
                    {
                        CheckBackendBounds(schema, parameter, singleValue,
                            bound => bound.Float32Value, issues);
                        return issues;
                    }
                    break;

                case TerraKitBackendParameterKind.Float64:
                    double doubleValue;
                    if (double.TryParse(text, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out doubleValue) &&
                        !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue))
                    {
                        CheckBackendBounds(schema, parameter, doubleValue,
                            bound => bound.Float64Value, issues);
                        return issues;
                    }
                    break;
            }

            issues.Add(new TerraKitParameterValidationIssue(
                parameter.key,
                "Value is not a valid " +
                TerraKitBackendParameterRules.NumericTypeName(schema.Kind) +
                ". Current value: " + FormatCurrentValue(text) + "."));
            return issues;
        }

        // Compare in the original numeric type. In particular, do not
        // convert 64-bit integers to double: adjacent large integers can round alike.
        private static void CheckBackendBounds<T>(
            TerraKitBackendParameter schema,
            TerraKitParameterData parameter,
            T value,
            Func<TerraKitBackendParameterValue, T> readBound,
            List<TerraKitParameterValidationIssue> issues)
            where T : IComparable<T>
        {
            if (schema.MinimumValue != null &&
                value.CompareTo(readBound(schema.MinimumValue)) < 0)
            {
                issues.Add(new TerraKitParameterValidationIssue(
                    parameter.key,
                    "Value must be at least " + schema.MinimumValue.ToInvariantString() +
                    ". Current value: " + FormatCurrentValue(parameter.value) + "."));
                return;
            }

            if (schema.MaximumValue != null &&
                value.CompareTo(readBound(schema.MaximumValue)) > 0)
            {
                issues.Add(new TerraKitParameterValidationIssue(
                    parameter.key,
                    "Value must be at most " + schema.MaximumValue.ToInvariantString() +
                    ". Current value: " + FormatCurrentValue(parameter.value) + "."));
            }
        }

        private static string FormatCurrentValue(string value)
        {
            return string.IsNullOrEmpty(value) ? "<empty>" : value;
        }
    }
}
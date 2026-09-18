using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TerraKit.Editor
{
    internal static class TerraKitBackendCheckMenu
    {
        [MenuItem("Tools/TerraKit/Check Backend Connection", priority = 210)]
        private static void CheckBackendConnection()
        {
            TerraKitNativeStatus status = TerraKitNativeLibrary.CheckAvailability();
            string message = status.Summary + "\n\n" + status.Details;

            if (status.IsAvailable)
            {
                try
                {
                    IReadOnlyList<TerraKitBackendStageSchema> stages =
                        TerraKitBackendSchemaDiscovery.DiscoverBuiltInStages();

                    var details = new StringBuilder(message);
                    details.Append("\n\nBuilt-in stages: ").Append(stages.Count);
                    details.Append("\n\nFull port and parameter schema was written to the Console.");
                    message = details.ToString();

                    // Console retains the complete stage information.
                    Debug.Log("[TerraKit] " + message + "\n\n" + BuildSchemaReport(stages));

                    // The dialog only displays the connection summary.
                    EditorUtility.DisplayDialog("TerraKit Backend", message, "OK");
                }
                catch (Exception exception)
                {
                    message += "\n\nThe library loaded, but built-in stage discovery failed:\n" +
                               exception.Message;
                    Debug.LogException(exception);
                    EditorUtility.DisplayDialog("TerraKit Backend Schema Error", message, "OK");
                }

                return;
            }

            Debug.LogWarning(
                "[TerraKit] Backend check failed with status " +
                status.StatusCode + ".\n" + message);

            EditorUtility.DisplayDialog(
                "TerraKit Backend Not Ready",
                message +
                "\n\nNext step: install the native TerraKit library for the current platform, then run this check again.",
                "OK");
        }

        private static string BuildSchemaReport(
            IReadOnlyList<TerraKitBackendStageSchema> stages)
        {
            var report = new StringBuilder("Backend built-in schema");

            foreach (TerraKitBackendStageSchema stage in stages)
            {
                report
                    .Append("\n\n")
                    .Append(stage.TypeId)
                    .Append(" v")
                    .Append(stage.SchemaVersion)
                    .Append(" — ")
                    .Append(stage.DisplayName)
                    .Append(" [")
                    .Append(stage.Category)
                    .Append(']');

                foreach (TerraKitBackendInputPort input in stage.Inputs)
                {
                    report
                        .Append("\n  IN  ")
                        .Append(input.Id)
                        .Append(" : ")
                        .Append(input.ResourceKind)
                        .Append(input.IsOptional ? " (optional)" : " (required)");
                }

                foreach (TerraKitBackendOutputPort output in stage.Outputs)
                {
                    report
                        .Append("\n  OUT ")
                        .Append(output.Id)
                        .Append(" : ")
                        .Append(output.ResourceKind);
                }

                foreach (TerraKitBackendParameter parameter in stage.Parameters)
                {
                    report
                        .Append("\n  PARAM ")
                        .Append(parameter.Id)
                        .Append(" : ")
                        .Append(parameter.Kind);

                    if (parameter.DefaultValue != null)
                    {
                        report
                            .Append(" default=")
                            .Append(parameter.DefaultValue.ToInvariantString());
                    }

                    if (parameter.MinimumValue != null)
                    {
                        report
                            .Append(" min=")
                            .Append(parameter.MinimumValue.ToInvariantString());
                    }

                    if (parameter.MaximumValue != null)
                    {
                        report
                            .Append(" max=")
                            .Append(parameter.MaximumValue.ToInvariantString());
                    }

                    if (parameter.EnumOptions.Count > 0)
                    {
                        report.Append(" options=");
                        for (int index = 0; index < parameter.EnumOptions.Count; index++)
                        {
                            if (index > 0)
                            {
                                report.Append(',');
                            }

                            report.Append(parameter.EnumOptions[index].Id);
                        }
                    }
                }
            }

            return report.ToString();
        }
    }
}
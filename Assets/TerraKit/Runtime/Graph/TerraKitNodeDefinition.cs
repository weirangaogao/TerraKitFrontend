using System.Collections.Generic;
using System;

namespace TerraKit
{
    /// <summary>
    /// Defines a node type. Runtime, compiler, and editor can all read this without depending on GraphView.
    /// </summary>
    public interface ITerraKitNodeDefinition
    {
        string TypeId { get; }
        uint SchemaVersion { get; }
        string DisplayName { get; }
        string Category { get; }
        string Description { get; }
        IReadOnlyList<TerraKitPortDefinition> Inputs { get; }
        IReadOnlyList<TerraKitPortDefinition> Outputs { get; }
        IReadOnlyList<TerraKitParameterDefinition> Parameters { get; }
    }

    public sealed class TerraKitNodeDefinition : ITerraKitNodeDefinition
    {
        public string TypeId { get; private set; }
        public uint SchemaVersion { get; private set; }
        public string DisplayName { get; private set; }
        public string Category { get; private set; }
        public string Description { get; private set; }
        public IReadOnlyList<TerraKitPortDefinition> Inputs { get; private set; }
        public IReadOnlyList<TerraKitPortDefinition> Outputs { get; private set; }
        public IReadOnlyList<TerraKitParameterDefinition> Parameters { get; private set; }

        public TerraKitNodeDefinition(
            string typeId,
            string displayName,
            string category,
            IReadOnlyList<TerraKitPortDefinition> inputs,
            IReadOnlyList<TerraKitPortDefinition> outputs,
            IReadOnlyList<TerraKitParameterDefinition> parameters,
            string description = "",
            uint schemaVersion = 1)
        {
            TypeId = typeId;
            SchemaVersion = schemaVersion;
            DisplayName = displayName;
            Category = category;
            Description = description ?? string.Empty;
            Inputs = inputs;
            Outputs = outputs;
            Parameters = parameters;
        }
    }

    public sealed class TerraKitPortDefinition
    {
        public string Id { get; private set; }
        public string DisplayName { get; private set; }
        public TerraKitPortType Type { get; private set; }
        public bool IsRequired { get; private set; }
        public bool AllowMultipleConnections { get; private set; }

        public TerraKitPortDefinition(
            string id,
            string displayName,
            TerraKitPortType type,
            bool isRequired,
            bool allowMultipleConnections)
        {
            Id = id;
            DisplayName = displayName;
            Type = type;
            IsRequired = isRequired;
            AllowMultipleConnections = allowMultipleConnections;
        }
    }

    public sealed class TerraKitParameterDefinition
    {
        public string Key { get; private set; }
        public string DisplayName { get; private set; }
        public TerraKitParameterType Type { get; private set; }
        public string DefaultValue { get; private set; }
        public string Description { get; private set; }
        public string ValidValueHint { get; private set; }
        public TerraKitBackendParameter BackendParameter { get; private set; }

        public TerraKitParameterDefinition(
            string key,
            string displayName,
            TerraKitParameterType type,
            string defaultValue,
            string description,
            TerraKitBackendParameter backendParameter)
        {
            Key = key;
            DisplayName = displayName;
            Type = type;
            DefaultValue = defaultValue;
            Description = description ?? string.Empty;

            BackendParameter = backendParameter ??
                throw new ArgumentNullException(nameof(backendParameter));

            ValidValueHint = TerraKitBackendParameterRules.BuildHint(BackendParameter);
        }
    }
}
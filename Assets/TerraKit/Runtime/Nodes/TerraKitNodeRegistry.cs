using System.Collections.Generic;
using System.Linq;

namespace TerraKit
{
    public static class TerraKitNodeRegistry
    {
        private sealed class BackendSnapshot
        {
            public IReadOnlyList<TerraKitBackendStageSchema> Schemas;
            public Dictionary<string, TerraKitBackendStageSchema> SchemasById;
            public Dictionary<string, ITerraKitNodeDefinition> DefinitionsById;
            public IReadOnlyList<ITerraKitNodeDefinition> All;
        }

        private static BackendSnapshot _backend;
        private static bool _loadAttempted;
        private static string _backendError;

        public static bool TryLoadBackend(out string error)
        {
            if (!_loadAttempted)
            {
                _loadAttempted = true;

                try
                {
                    _backend = BuildSnapshot(
                        TerraKitBackendSchemaDiscovery.DiscoverBuiltInStages());

                    _backendError = null;
                }
                catch (System.Exception exception)
                {
                    _backend = null;
                    _backendError = exception.Message;
                }
            }

            error = _backendError;
            return _backend != null;
        }

        public static bool RefreshBackend(out string error)
        {
            _backend = null;
            _backendError = null;
            _loadAttempted = false;
            return TryLoadBackend(out error);
        }

        private static BackendSnapshot BuildSnapshot(
            IReadOnlyList<TerraKitBackendStageSchema> schemas)
        {
            var byId =
                new Dictionary<string, TerraKitBackendStageSchema>();

            var definitions =
                new Dictionary<string, ITerraKitNodeDefinition>();

            var all =
                new List<ITerraKitNodeDefinition>();

            foreach (var schema in schemas)
            {
                if (string.IsNullOrEmpty(schema.TypeId) ||
                    byId.ContainsKey(schema.TypeId))
                {
                    throw new System.InvalidOperationException(
                        "Backend returned an empty or duplicate stage id: " +
                        schema.TypeId);
                }

                var definition = TerraKitBackendNodeAdapter.Create(schema);

                byId.Add(schema.TypeId, schema);
                definitions.Add(schema.TypeId, definition);
                all.Add(definition);
            }

            return new BackendSnapshot
            {
                Schemas = schemas.ToList().AsReadOnly(),
                SchemasById = byId,
                DefinitionsById = definitions,
                All = all.AsReadOnly()
            };
        }

        public static IReadOnlyList<TerraKitBackendStageSchema> BackendSchemas
        {
            get
            {
                string error;

                if (!TryLoadBackend(out error))
                {
                    throw new System.InvalidOperationException(
                        "Cannot read backend node registry: " + error);
                }

                return _backend.Schemas;
            }
        }

        public static IReadOnlyList<ITerraKitNodeDefinition> All
        {
            get
            {
                string error;

                return TryLoadBackend(out error)
                    ? _backend.All
                    : new ITerraKitNodeDefinition[0];
            }
        }

        public static bool TryGet(
            string typeId,
            out ITerraKitNodeDefinition definition)
        {
            definition = null;

            string error;

            return !string.IsNullOrEmpty(typeId) &&
                   TryLoadBackend(out error) &&
                   _backend.DefinitionsById.TryGetValue(
                       typeId, out definition);
        }

        public static bool TryGetBackendSchema(
            string typeId,
            out TerraKitBackendStageSchema schema)
        {
            schema = null;
            string error;

            return !string.IsNullOrEmpty(typeId) &&
                   TryLoadBackend(out error) &&
                   _backend.SchemasById.TryGetValue(typeId, out schema);
        }

        public static bool IsBackendExecutable(string typeId)
        {
            TerraKitBackendStageSchema schema;
            return TryGetBackendSchema(typeId, out schema);
        }

        public static bool IsOutput(ITerraKitNodeDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return definition.Outputs.Any(
                port => port.Type == TerraKitPortType.Mesh);
        }

        public static bool ArePortTypesCompatible(
            TerraKitPortType outputType,
            TerraKitPortType inputType)
        {
            return outputType == inputType;
        }

    }
}
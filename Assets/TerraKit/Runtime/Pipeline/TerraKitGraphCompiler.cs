using System.Collections.Generic;
using System.Linq;

namespace TerraKit
{
    // Checks graph and calculate the pipeline execution order
    public sealed class TerraKitGraphCompiler
    {
        public TerraKitGraphCompileResult Compile(TerraKitGraphAsset graph)
        {
            var result = new TerraKitGraphCompileResult();

            // Reject invalid graph assets before accessing their contents.
            if (graph == null)
            {
                result.AddError("No TerraKit graph asset is selected.");
                return result;
            }

            if (graph.nodes == null || graph.edges == null)
            {
                result.AddError("Graph node or edge collection is missing.");
                return result;
            }
            if (graph.nodes.Count == 0)
            {
                result.AddError("Add at least one backend stage before generating.");
                return result;
            }

            if (graph.nodes.Count > 0)
            {
                string error;

                // Load the backend metadata required for graph validation.
                if (!TerraKitNodeRegistry.TryLoadBackend(out error))
                {
                    result.AddError(
                        "Cannot read backend node registry: " + error);
                    return result;
                }
            }

            // Validate structural integrity before running dependent checks.
            ValidateGraphShape(graph, result);

            if (!result.Success)
            {
                return result;
            }

            ValidateParameters(graph, result);
            ValidateRequiredInputs(graph, result);
            BuildExecutionOrder(graph, result);

            return result;
        }

        private static void ValidateGraphShape(TerraKitGraphAsset graph, TerraKitGraphCompileResult result)
        {
            // Track node IDs to enforce uniqueness.
            var nodeIds = new HashSet<string>();

            foreach (var node in graph.nodes)
            {
                if (node == null)
                {
                    result.AddError("Graph contains a null node entry.");
                    continue;
                }

                if (string.IsNullOrEmpty(node.id))
                {
                    result.AddError("Graph contains a node with no id.");
                    continue;
                }

                if (!nodeIds.Add(node.id))
                {
                    result.AddError("Duplicate node id: " + node.id);
                }

                if (node.parameters == null || node.parameters.Any(p => p == null) ||
                    node.parameters.GroupBy(p => p.key).Any(g => g.Count() > 1))
                {
                    result.AddError(node.DisplayLabel + " has missing or duplicate parameter entries.", node.id);
                }

                ITerraKitNodeDefinition definition;
                if (!TerraKitNodeRegistry.TryGet(node.typeId, out definition))
                {
                    result.AddError("Unknown node type: " + node.typeId + " on node " + node.DisplayLabel, node.id);
                }
                else if (node.schemaVersion != definition.SchemaVersion)
                {
                    result.AddError(node.DisplayLabel + " uses schema v" + node.schemaVersion +
                        ", but the backend provides v" + definition.SchemaVersion +
                        ". Migrate or recreate this node before generating.", node.id);
                }
            }

            // Ensure each input port has at most one incoming connection.
            var connectedInputs = new HashSet<string>();
            foreach (var edge in graph.edges)
            {
                if (edge == null)
                {
                    result.AddError("Graph contains a null edge entry.");
                    continue;
                }

                if (!connectedInputs.Add(edge.inputNodeId + "\n" + edge.inputPortId))
                {
                    var target = graph.nodes.FirstOrDefault(n => n != null && n.id == edge.inputNodeId);
                    result.AddError((target != null ? target.DisplayLabel : edge.inputNodeId) +
                        " has an input port with more than one incoming connection.", edge.inputNodeId);
                }

                // Resolve both endpoints before validating the connection.
                var outputNode = graph.nodes.FirstOrDefault(n => n != null && n.id == edge.outputNodeId);
                var inputNode = graph.nodes.FirstOrDefault(n => n != null && n.id == edge.inputNodeId);

                if (outputNode == null)
                {
                    result.AddError("Edge references missing output node: " + edge.outputNodeId);
                    continue;
                }

                if (inputNode == null)
                {
                    result.AddError("Edge references missing input node: " + edge.inputNodeId);
                    continue;
                }

                ITerraKitNodeDefinition outputDefinition;
                ITerraKitNodeDefinition inputDefinition;
                if (!TerraKitNodeRegistry.TryGet(outputNode.typeId, out outputDefinition) ||
                    !TerraKitNodeRegistry.TryGet(inputNode.typeId, out inputDefinition))
                {
                    continue;
                }

                // Verify that the referenced ports exist and use compatible types.
                var outputPort = outputDefinition.Outputs.FirstOrDefault(p => p.Id == edge.outputPortId);
                var inputPort = inputDefinition.Inputs.FirstOrDefault(p => p.Id == edge.inputPortId);

                if (outputPort == null)
                {
                    result.AddError(outputNode.DisplayLabel + " has no output port named " + edge.outputPortId, outputNode.id);
                    continue;
                }

                if (inputPort == null)
                {
                    result.AddError(inputNode.DisplayLabel + " has no input port named " + edge.inputPortId, inputNode.id);
                    continue;
                }

                if (!TerraKitNodeRegistry.ArePortTypesCompatible(outputPort.Type, inputPort.Type))
                {
                    result.AddError(
                        outputNode.DisplayLabel + "." + outputPort.DisplayName +
                        " (" + outputPort.Type + ") cannot connect to " +
                        inputNode.DisplayLabel + "." + inputPort.DisplayName +
                        " (" + inputPort.Type + ").");
                }
            }
        }

        private static void ValidateParameters(
            TerraKitGraphAsset graph,
            TerraKitGraphCompileResult result)
        {
            // Validate each stored value against its parameter definition.
            foreach (var node in graph.nodes)
            {
                ITerraKitNodeDefinition definition;

                if (!TerraKitNodeRegistry.TryGet(
                    node.typeId, out definition))
                {
                    continue;
                }

                foreach (var parameterDefinition in definition.Parameters)
                {
                    var parameter = node.parameters.FirstOrDefault(
                        item => item.key == parameterDefinition.Key);

                    var issues = TerraKitParameterValidator.Validate(parameterDefinition, parameter);

                    foreach (var issue in issues)
                    {
                        result.AddError(
                            node.DisplayLabel + "." +
                            parameterDefinition.DisplayName +
                            ": " + issue.Message,
                            node.id);
                    }
                }
            }
        }

        private static void ValidateRequiredInputs(TerraKitGraphAsset graph, TerraKitGraphCompileResult result)
        {
            // Ensure every required input port is connected.
            foreach (var node in graph.nodes)
            {
                ITerraKitNodeDefinition definition;
                if (!TerraKitNodeRegistry.TryGet(node.typeId, out definition))
                {
                    continue;
                }

                foreach (var input in definition.Inputs.Where(p => p.IsRequired))
                {
                    bool hasConnection = graph.edges.Any(e => e.inputNodeId == node.id && e.inputPortId == input.Id);
                    if (!hasConnection)
                    {
                        result.AddError(
                            node.DisplayLabel + " is missing required input: " + input.DisplayName,
                            node.id);
                    }
                }
            }
        }

        private static void BuildExecutionOrder(TerraKitGraphAsset graph, TerraKitGraphCompileResult result)
        {
            // Build the lookup tables required for topological sorting.
            var nodesById = graph.nodes.ToDictionary(n => n.id, n => n);
            var incomingCount = graph.nodes.ToDictionary(n => n.id, n => 0);
            var outgoing = graph.nodes.ToDictionary(n => n.id, n => new List<string>());

            foreach (var edge in graph.edges)
            {
                if (!nodesById.ContainsKey(edge.outputNodeId) || !nodesById.ContainsKey(edge.inputNodeId))
                {
                    continue;
                }

                incomingCount[edge.inputNodeId]++;
                outgoing[edge.outputNodeId].Add(edge.inputNodeId);
            }

            // Begin with nodes that have no unresolved dependencies.
            var ready = new Queue<string>(incomingCount.Where(pair => pair.Value == 0).Select(pair => pair.Key));
            var sortedIds = new List<string>();

            while (ready.Count > 0)
            {
                string id = ready.Dequeue();
                sortedIds.Add(id);

                // Release downstream nodes as their dependencies are resolved.
                foreach (string next in outgoing[id])
                {
                    incomingCount[next]--;
                    if (incomingCount[next] == 0)
                    {
                        ready.Enqueue(next);
                    }
                }
            }

            // A partial sort indicates that the graph contains a cycle.
            if (sortedIds.Count != graph.nodes.Count)
            {
                result.AddError("Graph contains a cycle. TerraKit pipelines must be acyclic dataflow graphs.");
                return;
            }

            foreach (string id in sortedIds)
            {
                result.ExecutionOrder.Add(nodesById[id]);
            }
        }
    }
}
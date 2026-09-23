using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace TerraKit.Editor
{
    internal sealed class TerraKitNodeSearchProvider
        : ScriptableObject, ISearchWindowProvider
    {
        private TerraKitGraphView _graphView;
        private Vector2 _graphPosition;

        public void Initialize(
            TerraKitGraphView graphView, Vector2 graphPosition)
        {
            _graphView = graphView;
            _graphPosition = graphPosition;
        }

        public List<SearchTreeEntry> CreateSearchTree(
            SearchWindowContext context)
        {
            var backendDefinitions = TerraKitNodeRegistry.All;
            var tree = new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(
                    new GUIContent("Create TerraKit Node"), 0)
            };

            string backendError;
            if (!TerraKitNodeRegistry.TryLoadBackend(out backendError))
            {
                tree.Add(new SearchTreeEntry(new GUIContent(
                    "Backend unavailable",
                    backendError))
                {
                    level = 1
                });

                Debug.LogWarning(
                    "[TerraKit] Cannot list backend nodes: " + backendError);

                return tree;
            }

            foreach (string category in GetOrderedCategories(backendDefinitions))
            {
                var categoryDefinitions = backendDefinitions
                    .Where(definition => definition.Category == category)
                    .OrderBy(definition => definition.DisplayName)
                    .ToList();

                if (categoryDefinitions.Count == 0)
                    continue;

                tree.Add(new SearchTreeGroupEntry(
                    new GUIContent(category), 1));

                AddDefinitionEntries(tree, categoryDefinitions, 2);
            }

            return tree;
        }

        public bool OnSelectEntry(
            SearchTreeEntry entry, SearchWindowContext context)
        {
            var definition = entry.userData as ITerraKitNodeDefinition;
            if (_graphView == null || definition == null)
                return false;

            _graphView.CreateNode(definition, _graphPosition);
            return true;
        }

        private static void AddDefinitionEntries(
            ICollection<SearchTreeEntry> tree,
            IEnumerable<ITerraKitNodeDefinition> definitions,
            int level)
        {
            foreach (var definition in definitions)
            {
                tree.Add(new SearchTreeEntry(
                    new GUIContent(definition.DisplayName))
                {
                    level = level,
                    userData = definition
                });
            }
        }

        private static IEnumerable<string> GetOrderedCategories(
            IEnumerable<ITerraKitNodeDefinition> definitions)
        {
            return definitions
                .Select(definition => definition.Category)
                .Distinct()
                .OrderBy(category => category);
        }
    }
}
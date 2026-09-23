using System;
using System.Collections.Generic;
using UnityEngine;

// Defines data structures used to represent the graph

namespace TerraKit
{
    [Serializable]
    public sealed class TerraKitNodeData
    {
        public string id;
        public string typeId;
        public uint schemaVersion = 1;
        public string displayName;
        public Vector2 position;
        public Vector2 size;
        public List<TerraKitParameterData> parameters = new List<TerraKitParameterData>();

        // Creates a readable label using the node name and part of its ID.
        public string DisplayLabel
        {
            get
            {
                string name = string.IsNullOrEmpty(displayName) ? typeId : displayName;
                return string.IsNullOrEmpty(id) ? name :
                    name + " [" + id.Substring(0, Math.Min(8, id.Length)) + "]";
            }
        }
    }

    [Serializable]
    public sealed class TerraKitEdgeData
    {
        public string id;
        public string outputNodeId;
        public string outputPortId;
        public string inputNodeId;
        public string inputPortId;
    }

    [Serializable]
    public sealed class TerraKitParameterData
    {
        public string key;
        public string displayName;
        public TerraKitParameterType type;
        public string value;
    }
}
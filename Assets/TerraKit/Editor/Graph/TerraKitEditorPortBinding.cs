using UnityEditor.Experimental.GraphView;

namespace TerraKit.Editor
{
    internal sealed class TerraKitEditorPortBinding
    {
        public readonly string NodeId;
        public readonly string PortId;
        public readonly TerraKitPortType Type;
        public readonly Direction Direction;

        public TerraKitEditorPortBinding(string nodeId, string portId, TerraKitPortType type, Direction direction)
        {
            NodeId = nodeId;
            PortId = portId;
            Type = type;
            Direction = direction;
        }
    }
}

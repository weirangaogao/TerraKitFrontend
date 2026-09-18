using System.Collections.Generic;

namespace TerraKit
{
    public sealed class TerraKitGraphValidationException : System.InvalidOperationException
    {
        public TerraKitGraphCompileResult Result { get; }

        public TerraKitGraphValidationException(TerraKitGraphCompileResult result)
            : base("The graph is not ready for backend generation:\n" + string.Join("\n", result.Errors))
        {
            Result = result;
        }
    }

    public sealed class TerraKitGraphCompileIssue
    {
        public string Message { get; private set; }
        public string NodeId { get; private set; }

        public TerraKitGraphCompileIssue(string message, string nodeId = null)
        {
            Message = message;
            NodeId = nodeId;
        }
    }

    public sealed class TerraKitGraphCompileResult
    {
        public bool Success
        {
            get { return Errors.Count == 0; }
        }

        public readonly List<string> Errors = new List<string>();
        public readonly List<TerraKitGraphCompileIssue> ErrorIssues =
            new List<TerraKitGraphCompileIssue>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<TerraKitNodeData> ExecutionOrder = new List<TerraKitNodeData>();

        public void AddError(string message, string nodeId = null)
        {
            Errors.Add(message);
            ErrorIssues.Add(new TerraKitGraphCompileIssue(message, nodeId));
        }
    }
}
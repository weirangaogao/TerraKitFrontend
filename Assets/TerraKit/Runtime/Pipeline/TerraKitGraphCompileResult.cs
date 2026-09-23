using System.Collections.Generic;
using System.Linq;

namespace TerraKit
{
    public sealed class TerraKitGraphValidationException : System.InvalidOperationException
    {
        public TerraKitGraphCompileResult Result { get; }

        public TerraKitGraphValidationException(TerraKitGraphCompileResult result)
            : base("The graph is not ready for backend generation:\n" + string.Join("\n", result.ErrorIssues.Select(issue => issue.Message)))
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
            get { return ErrorIssues.Count == 0; }
        }

        public readonly List<TerraKitGraphCompileIssue> ErrorIssues = new List<TerraKitGraphCompileIssue>();
        public readonly List<TerraKitNodeData> ExecutionOrder = new List<TerraKitNodeData>();

        public void AddError(string message, string nodeId = null)
        {
            ErrorIssues.Add(new TerraKitGraphCompileIssue(message, nodeId));
        }
    }
}
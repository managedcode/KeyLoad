namespace KeyLoad.Server;

/// <summary>Identifies only the three fixed public gateway control tools.</summary>
internal enum McpGatewayMetaOperation : byte
{
    None,
    Search,
    Route,
    Invoke
}

/// <summary>Contains the exact inner canonical operation selected before payload decoding.</summary>
internal readonly record struct McpGatewayMetaSelection(
    McpGatewayMetaOperation Operation,
    McpOperationDescriptor? CanonicalOperation);

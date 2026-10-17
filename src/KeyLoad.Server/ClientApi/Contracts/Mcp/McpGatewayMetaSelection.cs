namespace KeyLoad.Server;

/// <summary>Contains the exact inner canonical operation selected before payload decoding.</summary>
internal readonly record struct McpGatewayMetaSelection(
    McpGatewayMetaOperation Operation,
    McpOperationDescriptor? CanonicalOperation);

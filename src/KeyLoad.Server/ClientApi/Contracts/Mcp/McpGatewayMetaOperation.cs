namespace KeyLoad.Server;

/// <summary>Identifies only the three fixed public gateway control tools.</summary>
internal enum McpGatewayMetaOperation : byte
{
    None,
    Search,
    Route,
    Invoke
}

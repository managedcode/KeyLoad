namespace KeyLoad.Server;

/// <summary>Owned bounded arguments for one gateway meta operation.</summary>
internal sealed record McpGatewayMetaRequest(
    McpGatewayMetaOperation Operation,
    string? Query = null,
    int SearchLimit = McpGatewayMetaProtocol.DefaultSearchLimit,
    int CategoryLimit = McpGatewayMetaProtocol.DefaultRouteLimit,
    int ToolsPerCategory = McpGatewayMetaProtocol.DefaultRouteLimit,
    bool? PreferReadOnly = null,
    string? ToolId = null,
    IReadOnlyDictionary<string, object?>? Arguments = null);

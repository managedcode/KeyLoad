using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KeyLoad.Server;

/// <summary>Native official MCP handlers registered by the shared server composition root.</summary>
internal static class McpAgentGuideHandlers
{
    internal static ValueTask<ListResourcesResult> ListResourcesAsync(
        RequestContext<ListResourcesRequestParams> request, CancellationToken cancellationToken)
        => ValueTask.FromResult(McpAgentGuideQuery.ListResources(request.Params?.Cursor, cancellationToken));

    internal static ValueTask<ReadResourceResult> ReadResourceAsync(
        RequestContext<ReadResourceRequestParams> request, CancellationToken cancellationToken)
        => ValueTask.FromResult(McpAgentGuideQuery.ReadResource(request.Params?.Uri, cancellationToken));

    internal static ValueTask<ListPromptsResult> ListPromptsAsync(
        RequestContext<ListPromptsRequestParams> request, CancellationToken cancellationToken)
        => ValueTask.FromResult(McpAgentGuideQuery.ListPrompts(request.Params?.Cursor, cancellationToken));

    internal static ValueTask<GetPromptResult> GetPromptAsync(
        RequestContext<GetPromptRequestParams> request, CancellationToken cancellationToken)
        => ValueTask.FromResult(McpAgentGuideQuery.GetPrompt(request.Params?.Name,
            request.Params?.Arguments, cancellationToken));
}

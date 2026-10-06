using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KeyLoad.Server;

/// <summary>Native official MCP handlers registered by the shared server composition root.</summary>
internal static class McpAgentGuideHandlers
{
    internal static ValueTask<ListResourcesResult> ListResourcesAsync(RequestContext<ListResourcesRequestParams> request, IOptions<McpExecutionOptions> executionOptions, CancellationToken cancellationToken)
        => ValueTask.FromResult(McpAgentGuideQuery.ListResources(cursor: request.Params?.Cursor, cancellationToken: cancellationToken, executionOptions: executionOptions));

    internal static ValueTask<ReadResourceResult> ReadResourceAsync(RequestContext<ReadResourceRequestParams> request, IOptions<McpExecutionOptions> executionOptions, CancellationToken cancellationToken)
        => ValueTask.FromResult(McpAgentGuideQuery.ReadResource(uri: request.Params?.Uri, cancellationToken: cancellationToken, executionOptions: executionOptions));

    internal static ValueTask<ListPromptsResult> ListPromptsAsync(RequestContext<ListPromptsRequestParams> request, IOptions<McpExecutionOptions> executionOptions, CancellationToken cancellationToken)
        => ValueTask.FromResult(McpAgentGuideQuery.ListPrompts(cursor: request.Params?.Cursor, cancellationToken: cancellationToken, executionOptions: executionOptions));

    internal static ValueTask<GetPromptResult> GetPromptAsync(RequestContext<GetPromptRequestParams> request, IOptions<McpExecutionOptions> executionOptions, CancellationToken cancellationToken)
        => ValueTask.FromResult(McpAgentGuideQuery.GetPrompt(name: request.Params?.Name,
            arguments: request.Params?.Arguments, cancellationToken: cancellationToken, executionOptions: executionOptions));
}

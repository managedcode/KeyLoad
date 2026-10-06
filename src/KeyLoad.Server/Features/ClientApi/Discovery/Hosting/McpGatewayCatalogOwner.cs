using ManagedCode.MCPGateway;
using ManagedCode.MCPGateway.Abstractions;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Owns one bounded native graph instance over immutable canonical operation metadata.</summary>
internal sealed class McpGatewayCatalogOwner : IAsyncDisposable
{
    private const string SourceId = "keyload";
    private const string IndexFailure = "The canonical MCP metadata index could not be initialized.";
    private const string InvocationFailure = "The canonical MCP operation did not produce a native result.";
    private readonly IMcpGatewayFactory _factory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly McpGatewayCatalogLifetime _lifetime;

    internal McpGatewayCatalogOwner(IMcpGatewayFactory factory, IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _factory = factory;
        _httpContextAccessor = httpContextAccessor;
        _lifetime = new McpGatewayCatalogLifetime(BuildInstanceAsync);
    }

    internal Task InitializeAsync(CancellationToken cancellationToken) => _lifetime.InitializeAsync(cancellationToken);

    internal async Task<McpGatewaySearchResult> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        McpGatewayCatalogRequestValidation.ValidateQuery(query);
        McpGatewayCatalogRequestValidation.ValidateSearchLimit(maxResults);
        using var lease = _lifetime.Acquire();
        var result = await lease.Instance.Gateway.SearchAsync(
            new McpGatewaySearchRequest(Query: query, MaxResults: maxResults), cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return result with { Diagnostics = [] };
    }

    internal async Task<McpGatewayToolRouteResult> RouteAsync(
        string query,
        int maxCategories,
        int maxToolsPerCategory,
        bool? preferReadOnly,
        CancellationToken cancellationToken)
    {
        McpGatewayCatalogRequestValidation.ValidateQuery(query);
        McpGatewayCatalogRequestValidation.ValidateRouteLimits(maxCategories, maxToolsPerCategory);
        using var lease = _lifetime.Acquire();
        var result = await lease.Instance.Gateway.RouteToolsAsync(
            new McpGatewayToolRouteRequest(
                Query: query,
                MaxCategories: maxCategories,
                MaxToolsPerCategory: maxToolsPerCategory,
                PreferReadOnly: preferReadOnly), cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return result with { Diagnostics = [] };
    }

    internal async Task<McpGatewayInvokeResult> InvokeAsync(
        string toolId,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!McpOperationCatalog.TryGetTool(toolId, out var operation) || operation is null)
        {
            throw Errors.Fail(ErrorCode.Validation, McpCatalogProtocol.InvalidArguments);
        }

        using var lease = _lifetime.Acquire();
        var result = await lease.Instance.Gateway.InvokeAsync(
            new McpGatewayInvokeRequest(ToolId: toolId, Arguments: arguments), cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!result.IsSuccess || result.Output is not CallToolResult)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, InvocationFailure);
        }

        return result;
    }

    public ValueTask DisposeAsync() => _lifetime.DisposeAsync();

    private async Task<IMcpGatewayInstance> BuildInstanceAsync(CancellationToken cancellationToken)
    {
        const int EmptyGraphNodeCount = 0;

        var entries = McpGatewayCatalogValidation.CreateEntries(McpOperationCatalog.Entries);
        cancellationToken.ThrowIfCancellationRequested();
        var instance = _factory.Create(CreateOptions(entries));
        try
        {
            var result = await instance.Gateway.BuildIndexAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!result.IsGraphSearchEnabled || result.ToolCount != entries.Length || result.GraphNodeCount == EmptyGraphNodeCount)
            {
                throw new InvalidOperationException(IndexFailure);
            }

            return instance;
        }
        catch (Exception failure)
        {
            try
            {
                await instance.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(failure, cleanupFailure);
            }

            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
    }

    private McpGatewayOptions CreateOptions(IReadOnlyList<McpGatewayCatalogEntry> entries)
    {
        var options = new McpGatewayOptions
        {
            SearchStrategy = McpGatewaySearchStrategy.Graph,
            MarkdownLdGraphSearchMode = McpGatewayMarkdownLdGraphSearchMode.SchemaAware,
            MarkdownLdGraphSource = McpGatewayMarkdownLdGraphSource.GeneratedToolGraph,
            SearchQueryNormalization = McpGatewaySearchQueryNormalization.Disabled,
            DefaultSearchLimit = McpGatewayCatalogValidation.DefaultSearchResults,
            MaxSearchResults = McpGatewayCatalogValidation.NativeMaximumResults,
            MaxDescriptorLength = McpGatewayCatalogValidation.MaximumDescriptorTextLength,
            MarkdownLdGraphSchemaSearchProfile = null
        };

        foreach (var entry in entries)
        {
            AITool tool = new McpGatewayCanonicalToolFunction(entry.Operation, _httpContextAccessor);
            options.AddTool(tool, entry.SearchHints, SourceId);
        }

        return options;
    }
}

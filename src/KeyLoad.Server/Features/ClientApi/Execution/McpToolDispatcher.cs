using System.Text.Json;
using KeyLoad.Orleans;
using ManagedCode.MCPGateway;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KeyLoad.Server;

/// <summary>Adapts admitted gateway operations to the existing signed Orleans execution path.</summary>
internal sealed class McpToolDispatcher(HttpContext context, McpRequestState state)
{
    private const string FailureLog = "MCP database operation failed with {ExceptionType}.";
    private const int FailureEventId = 1002;
    private static readonly Action<ILogger, string, Exception?> LogFailure = LoggerMessage.Define<string>(LogLevel.Error,
        new EventId(FailureEventId), FailureLog);

    /// <summary>Dispatches one admitted gateway meta operation, preserving native canonical results.</summary>
    internal async ValueTask<CallToolResult> CallAsync(RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
        try
        {
            var parameters = request.Params;
            if (parameters is null || !string.Equals(McpGatewayMetaProtocol.NameFor(state.MetaOperation),
                parameters.Name, StringComparison.Ordinal))
            { throw Errors.Fail(ErrorCode.Validation, McpGatewayMetaProtocol.InvalidArguments); }
            var meta = McpGatewayMetaArgumentReader.Read(state.MetaOperation, parameters.Arguments);
            return state.MetaOperation switch
            {
                McpGatewayMetaOperation.Search => await SearchAsync(meta, cancellationToken).ConfigureAwait(false),
                McpGatewayMetaOperation.Route => await RouteAsync(meta, cancellationToken).ConfigureAwait(false),
                McpGatewayMetaOperation.Invoke => await InvokeAsync(meta, cancellationToken).ConfigureAwait(false),
                _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, McpCatalogProtocol.InvalidOperation)
            };
        }
        catch (KeyLoadException error)
        { return state.Failure(error.Code, CanonicalOperationGateway.RequestId(context)); }
        catch (JsonException)
        { return state.Failure(ErrorCode.Validation, CanonicalOperationGateway.RequestId(context)); }
        catch (Exception error) when ((error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            && NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            LogFailure(context.RequestServices.GetRequiredService<ILogger<McpToolDispatcher>>(),
                error.GetType().FullName ?? error.GetType().Name, null);
            return state.Failure(ErrorCode.RecoveryRequired, CanonicalOperationGateway.RequestId(context));
        }
    }

    /// <summary>Invokes the selected typed canonical operation from the native local AIFunction adapter.</summary>
    internal async ValueTask<CallToolResult> InvokeCanonicalAsync(
        IDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        try
        {
            var descriptor = state.Operation;
            if (state.MetaOperation != McpGatewayMetaOperation.Invoke || descriptor is null)
            { throw Errors.Fail(ErrorCode.UnsupportedCapability, McpCatalogProtocol.InvalidOperation); }
            var operation = descriptor.IsAdapter
                ? SqlMcpCatalog.Decode(arguments, state.MaximumPayloadBytes,
                    context.RequestServices.GetRequiredService<KeyLoad.Core.DatabaseEngine>().Limits, cancellationToken)
                : descriptor.Decode(arguments, state.MaximumPayloadBytes);
            var reply = await CanonicalOperationGateway.ExecuteAsync(context, operation.ReadKind,
                operation.CommandKind, operation.CommandId, operation.Payload, cancellationToken).ConfigureAwait(false);
            return state.Success(reply, cancellationToken);
        }
        catch (KeyLoadException error)
        { return state.Failure(error.Code, CanonicalOperationGateway.RequestId(context)); }
        catch (JsonException)
        { return state.Failure(ErrorCode.Validation, CanonicalOperationGateway.RequestId(context)); }
        catch (Exception error) when ((error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            && NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            LogFailure(context.RequestServices.GetRequiredService<ILogger<McpToolDispatcher>>(),
                error.GetType().FullName ?? error.GetType().Name, null);
            return state.Failure(ErrorCode.RecoveryRequired, CanonicalOperationGateway.RequestId(context));
        }
    }

    /// <summary>Produces exactly the three bounded public meta tools without operation pagination.</summary>
    internal ValueTask<ListToolsResult> ListAsync(RequestContext<ListToolsRequestParams> request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(McpGatewayToolPagination.Create(request.Params?.Cursor, state.MaximumReplyBytes));
    }

    private async ValueTask<CallToolResult> SearchAsync(McpGatewayMetaRequest request, CancellationToken cancellationToken)
    {
        var gateway = context.RequestServices.GetRequiredService<McpGatewayCatalogOwner>();
        var result = await gateway.SearchAsync(request.Query!, request.SearchLimit, cancellationToken).ConfigureAwait(false);
        var projection = McpGatewayMetaProjector.Search(result, request.SearchLimit);
        var bytes = McpGatewayMetaResultWriter.Serialize(projection, state.MaximumReplyBytes);
        return state.MetaSuccess(bytes, cancellationToken);
    }

    private async ValueTask<CallToolResult> RouteAsync(McpGatewayMetaRequest request, CancellationToken cancellationToken)
    {
        var gateway = context.RequestServices.GetRequiredService<McpGatewayCatalogOwner>();
        var result = await gateway.RouteAsync(request.Query!, request.CategoryLimit, request.ToolsPerCategory,
            request.PreferReadOnly, cancellationToken).ConfigureAwait(false);
        var projection = McpGatewayMetaProjector.Route(result, request.CategoryLimit, request.ToolsPerCategory);
        var bytes = McpGatewayMetaResultWriter.Serialize(projection, state.MaximumReplyBytes);
        return state.MetaSuccess(bytes, cancellationToken);
    }

    private async ValueTask<CallToolResult> InvokeAsync(McpGatewayMetaRequest request, CancellationToken cancellationToken)
    {
        if (state.Operation is not { } descriptor || !string.Equals(descriptor.Name, request.ToolId, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.Validation, McpGatewayMetaProtocol.InvalidArguments); }
        var gateway = context.RequestServices.GetRequiredService<McpGatewayCatalogOwner>();
        var result = await gateway.InvokeAsync(request.ToolId!, request.Arguments!, cancellationToken).ConfigureAwait(false);
        if (McpGatewayNativeResult.GetOriginal(result) is { } native)
        { return native; }
        return state.Failure(ErrorCode.RecoveryRequired, CanonicalOperationGateway.RequestId(context));
    }
}

using System.Text.Json;
using KeyLoad.Orleans;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KeyLoad.Server;

/// <summary>Adapts admitted official tool calls to the same signed Orleans gateway used by HTTP.</summary>
internal sealed class McpToolDispatcher(HttpContext context, McpRequestState state)
{
    private const string FailureLog = "MCP database operation failed with {ExceptionType}.";
    private const int FailureEventId = 1002;
    private static readonly Action<ILogger, string, Exception?> LogFailure = LoggerMessage.Define<string>(LogLevel.Error,
        new EventId(FailureEventId), FailureLog);

    /// <summary>Decodes only an admitted capability and preserves the actual execution identity on every domain outcome.</summary>
    internal async ValueTask<CallToolResult> CallAsync(RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
        try
        {
            var descriptor = state.Operation;
            if (descriptor is null || request.Params is not { } parameters
                || !string.Equals(descriptor.Name, parameters.Name, StringComparison.Ordinal))
            { throw Errors.Fail(ErrorCode.Validation, McpCatalogProtocol.InvalidArguments); }
            var operation = descriptor.IsAdapter
                ? SqlMcpCatalog.Decode(parameters.Arguments, state.MaximumPayloadBytes,
                    context.RequestServices.GetRequiredService<KeyLoad.Core.DatabaseEngine>().Limits, cancellationToken)
                : descriptor.Decode(parameters.Arguments, state.MaximumPayloadBytes);
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

    /// <summary>Produces a byte-bounded native page from the immutable public operation catalog.</summary>
    internal ValueTask<ListToolsResult> ListAsync(RequestContext<ListToolsRequestParams> request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(McpToolPagination.Create(request.Params?.Cursor, state.MaximumReplyBytes));
    }
}

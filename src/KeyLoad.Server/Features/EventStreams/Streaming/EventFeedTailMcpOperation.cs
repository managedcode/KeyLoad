using ModelContextProtocol.Protocol;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventFeedTailMcpOperation
{
    private const string Invalid = "The event feed Tail lost its original operation identity.";
    internal static async ValueTask<CallToolResult> ExecuteAsync(HttpContext context, McpRequestState state,
        McpDecodedOperation operation, CancellationToken cancellationToken)
    {
        var request = NativeSerialization.Deserialize<EventFeedControlRequest>(operation.Payload.Span);
        EventFeedParentArguments.Require(request, EventFeedControlAction.Tail);
        var progress = EventFeedTailMcpProgressOwner.Require(context);
        var result = await CanonicalOperationGateway.ExecuteTailResultAsync(context, operation.CommandId,
            operation.Payload, (frame, token) => progress.SendAsync(frame, state, request.Limit, token),
            cancellationToken).ConfigureAwait(false);
        var requestId = CanonicalOperationGateway.RequestId(context)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        return state.Success(new(requestId, McpBoundedJson.Serialize(result, state.MaximumReplyBytes)), cancellationToken);
    }
}

using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class RetentionAckArchiveR53Actions
{
    internal static async Task AckAsync(RequestCqrsRf3Callers callers, RetentionAckArchiveR53State state,
        int index, long checkpoint, CancellationToken token)
    {
        var delivery = state.Claimed.Deliveries[index];
        var request = new SubscriptionDeliveryCommand(Guid.NewGuid(), state.Group, delivery.Token, DeliveryAction.Ack);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteSubscriptionAsync(request, token));
        await RetentionAckArchiveR53Current.ReceiptAsync(state, receipt, request.CommandId,
            new(RetentionAckArchiveR53Protocol.AckKind, state.Group.Source.Resource,
                delivery.Event.Position.ToString(System.Globalization.CultureInfo.InvariantCulture), 1));
        state.Checkpoint = checkpoint;
        state.Acks.Add((request, receipt));
        await RetentionAckArchiveR53Current.CaptureAsync(callers, state, token);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await callers.Mcp.CallAsync(McpCallerTools.SubscriptionsComplete, request, token))).Value);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
    }

    internal static async Task PauseAsync(RequestCqrsRf3Callers callers, RetentionAckArchiveR53State state,
        bool paused, CancellationToken token)
    {
        var request = new SetSubscriptionPausedRequest(Guid.NewGuid(), state.Group, 1, paused);
        var result = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.SetSubscriptionPausedAsync(request, token));
        state.Paused = paused;
        await SqlRf3Protocol.EqualAsync(RetentionAckArchiveR53Current.Expected(state), result);
        state.Pauses.Add((request, result));
        await RetentionAckArchiveR53Current.CaptureAsync(callers, state, token);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        await SqlRf3Protocol.EqualAsync(result, (await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
            await callers.Mcp.CallAsync(McpCallerTools.SubscriptionsPause, request, token))).Value);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
    }

    internal static async Task RefusedAsync(RequestCqrsRf3Callers callers, RetentionAckArchiveR53State state,
        bool capture, CancellationToken token)
    {
        var failure = await callers.Sdk.CommitAsync(state.Refused, token);
        await Assert.That(failure.IsSuccess).IsFalse();
        await Assert.That(failure.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.ResourceExhausted,
            RetentionAckArchiveR53Protocol.PinDetail), failure.Problem);
        if (capture)
        { state.Refusal = failure; }
        else
        { await SqlRf3Protocol.EqualAsync(state.Refusal, failure); }
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        var reply = await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Refused, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.ResourceExhausted, dispatched: true);
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(RetentionAckArchiveR53Protocol.SafeCapacity);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
    }
}

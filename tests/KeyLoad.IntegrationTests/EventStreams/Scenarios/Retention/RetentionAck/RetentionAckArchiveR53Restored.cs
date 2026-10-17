using KeyLoad.IntegrationTests.Features.BackupRestore;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class RetentionAckArchiveR53Restored
{
    internal static async Task PausedAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State original,
        CancellationToken token)
    {
        var state = original.AcknowledgedRetention ?? throw new InvalidOperationException(RetentionAckArchiveR53Protocol.Missing);
        await RetentionAckArchiveR53Current.CaptureAsync(callers, state, token);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        var old = new SubscriptionDeliveryCommand(Guid.NewGuid(), state.Group, state.Claimed.Deliveries[0].Token, DeliveryAction.Ack);
        await TargetInboxRf3Assertions.DeniedAsync(await callers.Sdk.CompleteSubscriptionAsync(old, token), ErrorCode.TokenInvalidated);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.SubscriptionsComplete, old, token),
            ErrorCode.TokenInvalidated, dispatched: true);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        await OldCommandAsync(callers, state, state.Original.PurgedPublish, token);
        await OldCommandAsync(callers, state, state.Original.Purge, token);
    }

    private static async Task OldCommandAsync(RequestCqrsRf3Callers callers, RetentionAckArchiveR53State state,
        CommandRequest request, CancellationToken token)
    {
        await TargetInboxRf3Assertions.DeniedAsync(await callers.Sdk.CommitAsync(request, token), ErrorCode.TokenInvalidated);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, request, token),
            ErrorCode.TokenInvalidated, dispatched: true);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
    }

    internal static async Task ContinuedAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State original,
        CancellationToken token)
    {
        var state = original.AcknowledgedRetention ?? throw new InvalidOperationException(RetentionAckArchiveR53Protocol.Missing);
        await RetentionAckArchiveR53Current.CaptureAsync(callers, state, token);
        state.CurrentResume = new(Guid.NewGuid(), state.Group, 1, false);
        state.Resumed = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.SetSubscriptionPausedAsync(state.CurrentResume, token));
        state.Paused = false;
        await SqlRf3Protocol.EqualAsync(RetentionAckArchiveR53Current.Expected(state), state.Resumed);
        await RetentionAckArchiveR53Current.CaptureAsync(callers, state, token);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        await SqlRf3Protocol.EqualAsync(state.Resumed, (await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
            await callers.Mcp.CallAsync(McpCallerTools.SubscriptionsPause, state.CurrentResume, token))).Value);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        state.Empty = new(Guid.NewGuid(), state.Group);
        state.EmptyResult = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveSubscriptionAsync(state.Empty, token));
        await Assert.That(state.EmptyResult.RequestId).IsEqualTo(state.Empty.RequestId);
        await Assert.That(state.EmptyResult.Deliveries).IsEmpty();
        await SqlRf3Protocol.EqualAsync(RetentionAckArchiveR53Current.Expected(state), state.EmptyResult.Status);
        await Assert.That(state.EmptyResult.Token.AtomicPartitionId).IsEqualTo(original.Partition.AtomicPartitionId);
        await Assert.That(state.EmptyResult.Token.Incarnation).IsEqualTo(state.Placement.Incarnation);
        await Assert.That(state.EmptyResult.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(state.EmptyResult.Token.Position).IsGreaterThan(0L);
        await RetentionAckArchiveR53Current.CaptureAsync(callers, state, token);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        await ColdAsync(callers, original, token);
    }

    internal static async Task ColdAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State original,
        CancellationToken token)
    {
        var state = original.AcknowledgedRetention ?? throw new InvalidOperationException(RetentionAckArchiveR53Protocol.Missing);
        await RetentionAckArchiveR53Current.CaptureAsync(callers, state, token);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        var resume = state.CurrentResume ?? throw new InvalidOperationException(RetentionAckArchiveR53Protocol.Missing);
        await SqlRf3Protocol.EqualAsync(state.Resumed, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.SetSubscriptionPausedAsync(resume, token)));
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        await SqlRf3Protocol.EqualAsync(state.Resumed, (await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
            await callers.Mcp.CallAsync(McpCallerTools.SubscriptionsPause, resume, token))).Value);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        var request = state.Empty ?? throw new InvalidOperationException(RetentionAckArchiveR53Protocol.Missing);
        await SqlRf3Protocol.EqualAsync(state.EmptyResult, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.ReceiveSubscriptionAsync(request, token)));
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        await SqlRf3Protocol.EqualAsync(state.EmptyResult, (await McpCallerAssertions.SuccessAsync<ReceiveSubscriptionResult>(
            await callers.Mcp.CallAsync(McpCallerTools.SubscriptionsReceive, request, token))).Value);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        await OldCommandAsync(callers, state, state.Original.PurgedPublish, token);
        await OldCommandAsync(callers, state, state.Original.Purge, token);
    }
}

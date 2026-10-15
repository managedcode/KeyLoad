using KeyLoad.IntegrationTests.Features.BackupRestore;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class UnacknowledgedArchiveRestored
{
    internal static async Task PausedAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State original,
        CancellationToken token)
    {
        var state = original.UnackedRetention ?? throw new InvalidOperationException(UnacknowledgedArchiveProtocol.Missing);
        state.Restored = true;
        await UnacknowledgedArchiveActions.ChangedAsync(callers, state, token);
        var old = new SubscriptionDeliveryCommand(Guid.NewGuid(), state.Group,
            state.SourceReceived.Deliveries[UnacknowledgedArchiveProtocol.FirstDelivery].Token, DeliveryAction.Ack);
        await UnacknowledgedArchiveFailures.RequireAsync(callers, state, McpCallerTools.SubscriptionsComplete,
            old, () => callers.Sdk.CompleteSubscriptionAsync(old, token), ErrorCode.TokenInvalidated,
            UnacknowledgedArchiveProtocol.InvalidSignedToken, UnacknowledgedArchiveProtocol.SafeFailure, token);
        await OldPublishAsync(callers, state, token);
        state.Replays.Clear();
    }

    private static async Task OldPublishAsync(RequestCqrsRf3Callers callers, UnacknowledgedArchiveState state,
        CancellationToken token)
    {
        await UnacknowledgedArchiveFailures.RequireAsync(callers, state, McpCallerTools.DocumentsCommit,
            state.Publish, () => callers.Sdk.CommitAsync(state.Publish, token), ErrorCode.TokenInvalidated,
            UnacknowledgedArchiveProtocol.EarlierIncarnation, UnacknowledgedArchiveProtocol.SafeFailure, token);
    }

    internal static async Task ContinuedAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State original,
        CancellationToken token)
    {
        var state = original.UnackedRetention ?? throw new InvalidOperationException(UnacknowledgedArchiveProtocol.Missing);
        await UnacknowledgedArchiveActions.ChangedAsync(callers, state, token);
        var first = UnacknowledgedArchiveActions.Refusal(state);
        state.CurrentFailures.Add(first);
        await UnacknowledgedArchiveActions.RefusedAsync(callers, state, first, token);
        await UnacknowledgedArchiveRepair.RunAsync(callers, state, token);
        state.Settled = true;
        await ColdAsync(callers, original, token);
    }

    internal static async Task ColdAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State original,
        CancellationToken token)
    {
        var state = original.UnackedRetention ?? throw new InvalidOperationException(UnacknowledgedArchiveProtocol.Missing);
        await Assert.That(state.Settled).IsTrue();
        await UnacknowledgedArchiveCurrent.RunAsync(callers, state, token);
        await UnacknowledgedArchiveReplay.AllAsync(callers, state, token);
        await UnacknowledgedArchiveHistory.RunAsync(callers, state, token);
        await OldPublishAsync(callers, state, token);
    }
}

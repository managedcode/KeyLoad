using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3EventingSourceOracle
{
    private const int SingleDelivery = 1;
    private const long LeasedStateVersion = 2;
    private const string EmptyHeaders = "{}";

    internal static async Task RequireAsync(KeyLoadClient sdk, ClusterRestoreRf3EventingState state,
        CancellationToken cancellationToken)
    {
        await Assert.That(state.QueueClaim.Deliveries.Length).IsEqualTo(SingleDelivery);
        await Assert.That(state.Processed.AlreadyProcessed).IsFalse();
        await SqlRf3Protocol.EqualAsync(state.Processed.Receipt.Token, state.Processed.OriginalEffectsToken);
        var ids = new[] { ClusterRestoreRf3EventingProtocol.First, ClusterRestoreRf3EventingProtocol.Second,
            ClusterRestoreRf3EventingProtocol.Third };
        await Assert.That(state.Page.Events.Length).IsEqualTo(ids.Length);
        for (var index = ClusterRestoreRf3EventingProtocol.FirstIndex; index < ids.Length; index++)
        {
            var actual = state.Page.Events[index];
            await Assert.That(actual.EventSequence).IsGreaterThan(ClusterRestoreRf3EventingProtocol.EmptyCheckpoint);
            await SqlRf3Protocol.EqualAsync(new SourceEventRecord(state.Source,
                index + ClusterRestoreRf3EventingProtocol.FirstPosition, actual.EventSequence,
                new(ids[index], ClusterRestoreRf3EventingProtocol.EventType, ClusterRestoreRf3EventingProtocol.Json),
                actual.RecordedAt), actual);
        }
        await SqlRf3Protocol.EqualAsync(new EventSourceHead(ClusterRestoreRf3EventingProtocol.LastSourcePosition,
            ClusterRestoreRf3EventingProtocol.FirstPosition, ClusterRestoreRf3EventingProtocol.InitialGeneration), state.Page.Head);
        await Assert.That(state.Page.HasMore).IsFalse();
        var claim = state.QueueClaim.Deliveries[ClusterRestoreRf3EventingProtocol.FirstIndex];
        await SqlRf3Protocol.EqualAsync(new MessageInspection(new(ClusterRestoreRf3EventingProtocol.MessageId,
            MessageState.Leased, SingleDelivery, LeasedStateVersion, ClusterRestoreRf3EventingProtocol.FirstPosition,
            null, null, PartitionMovementPublicParentRf3Administrator.PrincipalId,
            ClusterRestoreRf3EventingProtocol.InitialGeneration, claim.LeaseUntil),
            ClusterRestoreRf3EventingProtocol.Json, EmptyHeaders), state.Message);
        await SqlRf3Protocol.EqualAsync(Group(state, other: false), await McpCallerAssertions.SdkSuccessAsync(
            await sdk.SubscriptionStatusAsync(state.Group, cancellationToken).ConfigureAwait(false)));
        await SqlRf3Protocol.EqualAsync(Group(state, other: true), await McpCallerAssertions.SdkSuccessAsync(
            await sdk.SubscriptionStatusAsync(state.Other, cancellationToken).ConfigureAwait(false)));
    }

    internal static SubscriptionInfo Group(ClusterRestoreRf3EventingState state, bool other)
        => new(other ? state.Other : state.Group, new(PartitionMovementPublicParentRf3Administrator.PrincipalId),
            ClusterRestoreRf3EventingProtocol.InitialGeneration, ClusterRestoreRf3EventingProtocol.InitialOwnershipEpoch,
            other ? ClusterRestoreRf3EventingProtocol.EmptyCheckpoint : ClusterRestoreRf3EventingProtocol.FirstPosition,
            other ? ClusterRestoreRf3EventingProtocol.FirstPosition : ClusterRestoreRf3EventingProtocol.LastSourcePosition,
            ClusterRestoreRf3EventingProtocol.LastSourcePosition, false, null);
}

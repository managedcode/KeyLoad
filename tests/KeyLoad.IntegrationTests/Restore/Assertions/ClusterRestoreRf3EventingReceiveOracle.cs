using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3EventingReceiveOracle
{
    internal static async Task GroupAsync(ReceiveSubscriptionRequest request, ClusterRestoreRf3EventingState state,
        PhysicalShardRecord owner, ReceiveSubscriptionResult actual, bool healthy)
    {
        var expectedEvents = healthy ? state.FinalEvents.TakeLast(ClusterRestoreRf3EventingProtocol.SingleDelivery).ToArray()
            : state.Page.Events.ToArray();
        await Assert.That(actual.Deliveries.Length).IsEqualTo(expectedEvents.Length);
        var deliveries = System.Collections.Immutable.ImmutableArray.CreateBuilder<SubscriptionDelivery>(expectedEvents.Length);
        for (var index = ClusterRestoreRf3EventingProtocol.FirstIndex; index < expectedEvents.Length; index++)
        {
            var delivery = actual.Deliveries[index];
            await Assert.That(delivery.Token).IsNotEmpty();
            deliveries.Add(new(expectedEvents[index], delivery.Token, ClusterRestoreRf3EventingProtocol.InitialGeneration,
                delivery.LeaseUntil, ClusterRestoreRf3EventingProtocol.SingleDelivery));
        }
        await Assert.That(actual.Token.Position).IsGreaterThan(state.CurrentCommitPosition);
        var expectedTail = healthy ? ClusterRestoreRf3EventingProtocol.HealthyPosition : ClusterRestoreRf3EventingProtocol.LastSourcePosition;
        var status = new SubscriptionInfo(state.Group, new(PartitionMovementPublicParentRf3Administrator.PrincipalId),
            ClusterRestoreRf3EventingProtocol.ReconciledGeneration, ClusterRestoreRf3EventingProtocol.ReconciledOwnershipEpoch,
            healthy ? ClusterRestoreRf3EventingProtocol.LastSourcePosition : ClusterRestoreRf3EventingProtocol.EmptyCheckpoint,
            expectedTail, expectedTail, false, null);
        await SqlRf3Protocol.EqualAsync(new ReceiveSubscriptionResult(request.RequestId, deliveries.MoveToImmutable(), status,
            new(owner.Incarnation, state.Partition.AtomicPartitionId, actual.Token.Position, owner.PlacementEpoch)), actual);
        state.CurrentCommitPosition = actual.Token.Position;
    }

    internal static async Task QueueAsync(ReceiveRequest request, ClusterRestoreRf3EventingState state,
        PhysicalShardRecord owner, ReceiveResult actual, Delivery expected)
    {
        await Assert.That(actual.Token.Position).IsGreaterThan(state.CurrentCommitPosition);
        await SqlRf3Protocol.EqualAsync(new ReceiveResult(request.RequestId, [expected], new(owner.Incarnation,
            state.Partition.AtomicPartitionId, actual.Token.Position, owner.PlacementEpoch)), actual);
        state.CurrentCommitPosition = actual.Token.Position;
    }
}

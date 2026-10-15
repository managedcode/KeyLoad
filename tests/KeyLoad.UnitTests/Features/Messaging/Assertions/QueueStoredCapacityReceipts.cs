using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueStoredCapacityReceipts
{
    internal static async Task<CommitToken> NextAsync(DatabaseEngine owner, ZoneTreeStore store, PartitionRef partition)
    {
        var placement = owner.ReadAtomicPartitionPlacement(QueueEnqueueColdProtocol.Root, new(QueueEnqueueAuthorityProtocol.First, partition));
        await Assert.That(placement.Partition).IsEqualTo(partition);
        return new(placement.Incarnation, partition.AtomicPartitionId, checked(store.Position + QueueEnqueueAuthorityProtocol.First), placement.PlacementEpoch);
    }

    internal static async Task ReceiptAsync(OperationResult actual, Guid id, CommitToken token, ImmutableArray<MutationReceipt> mutations)
        => await QueueClaimVersionExhaustionAssertions.EqualAsync(actual.Get<CommitReceipt>(),
            new CommitReceipt(id, token, mutations, DurabilityProfile.ProcessDurable));

    internal static async Task<Delivery> ClaimAsync(DatabaseEngine owner, OperationResult actual,
        QueueStoredCapacityState state, PartitionRef partition, CommitToken token)
    {
        var delivery = await Assert.That(actual.Get<ReceiveResult>().Deliveries).HasSingleItem();
        var lane = new QueueLaneRef(partition, QueueEnqueueColdProtocol.Queue);
        var request = JsonDefaults.Deserialize<ReceiveRequest>(state.Receive.PayloadJson);
        var expected = new Delivery(QueueEnqueueColdProtocol.Ready, QueueEnqueueColdProtocol.ReadyJson,
            QueueEnqueueColdProtocol.Headers, delivery.Token, QueueEnqueueAuthorityProtocol.First,
            state.Receive.EvaluatedAt.AddSeconds(request.LeaseSeconds), QueueEnqueueAuthorityProtocol.First, QueueEnqueueAuthorityProtocol.First);
        await QueueClaimVersionExhaustionAssertions.EqualAsync(actual.Get<ReceiveResult>(),
            new ReceiveResult(state.Receive.Id, [expected], token));
        await Assert.That(delivery.Token).IsNotEqualTo(string.Empty);
        await QueueClaimVersionExhaustionAssertions.EqualAsync(owner.Verify<DeliveryClaims>(delivery.Token),
            new DeliveryClaims(lane, QueueEnqueueColdProtocol.Ready, QueueEnqueueColdProtocol.Root, QueueEnqueueAuthorityProtocol.First, QueueEnqueueAuthorityProtocol.First, token.Incarnation));
        return delivery;
    }

    internal static async Task FailureAsync(DatabaseEngine owner, ZoneTreeStore store, PartitionRef partition,
        ReplicatedOperation operation, OperationResult original)
    {
        var key = KeySpace.PartitionOutcome(partition, QueueEnqueueColdProtocol.Root, operation.Id);
        var retained = store.Read(view => view.ReadOwnedValue(key))!;
        await QueueClaimVersionExhaustionAssertions.EqualAsync(NativeSerialization.Deserialize<StoredOutcome>(retained).Result, original);
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        await QueueClaimVersionExhaustionAssertions.EqualAsync(owner.Apply(operation), original);
        await QueueClaimVersionExhaustionAssertions.EqualAsync(owner.ResolveOutcome(operation), original);
        await QueueStoredCapacityAssertions.ImageAsync(store, position, image);
        await Assert.That(store.Read(view => view.ReadOwnedValue(key))).IsEquivalentTo(retained, CollectionOrdering.Matching);
    }
}

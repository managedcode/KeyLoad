using System.Collections.Immutable;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueStoredCapacityReceipts
{
    internal static async Task<CommitToken> NextAsync(DatabaseEngine owner, ZoneTreeStore store, PartitionRef partition)
    {
        var placement = owner.ReadAtomicPartitionPlacement(QueueEnqueueColdProtocol.Root, partition);
        await Assert.That(placement.Partition).IsEqualTo(partition);
        await Assert.That(placement.Incarnation).IsEqualTo(store.Identity.Incarnation);
        return new(placement.Incarnation, partition.AtomicPartitionId, checked(owner.LastApplied + QueueEnqueueAuthorityProtocol.First));
    }

    internal static async Task ReceiptAsync(Result<object> actual, Guid id, CommitToken token, ImmutableArray<MutationReceipt> mutations)
        => await ReplayResultAssertions.Same<CommitReceipt>(actual, Result<object>.Succeed(new CommitReceipt(id, token, mutations, DurabilityProfile.ProcessDurable, 1)));

    internal static async Task<Delivery> ClaimAsync(DatabaseEngine owner, Result<object> actual,
        QueueStoredCapacityState state, PartitionRef partition, CommitToken token)
    {
        var delivery = await Assert.That(actual.Get<ReceiveResult>().Deliveries).HasSingleItem();
        var lane = new QueueLaneRef(partition, QueueEnqueueColdProtocol.Queue);
        var request = OrleansSerialization.Deserialize<ReceiveRequest>(state.Receive.Value!.Input.Span,
            ValidationProfile.PublicInputElements);
        var expected = new Delivery(QueueEnqueueColdProtocol.Ready, QueueEnqueueColdProtocol.ReadyJson,
            QueueEnqueueColdProtocol.Headers, delivery.Token, QueueEnqueueAuthorityProtocol.First,
            new DateTimeOffset(state.Receive.Timestamp).AddSeconds(request.LeaseSeconds), QueueEnqueueAuthorityProtocol.First, QueueEnqueueAuthorityProtocol.First);
        await ReplayResultAssertions.Same<ReceiveResult>(actual, Result<object>.Succeed(new ReceiveResult(state.Receive.CommandId, [expected], token)));
        await Assert.That(delivery.Token).IsNotEqualTo(string.Empty);
        await Assert.That(owner.Verify<DeliveryClaims>(delivery.Token)).IsEqualTo(
            new DeliveryClaims(lane, QueueEnqueueColdProtocol.Ready, QueueEnqueueColdProtocol.Root, QueueEnqueueAuthorityProtocol.First, QueueEnqueueAuthorityProtocol.First, token.Incarnation));
        return delivery;
    }

    internal static async Task FailureAsync(DatabaseEngine owner, ZoneTreeStore store, PartitionRef partition,
        Command<DatabaseCommand> operation, Result<object> original)
    {
        var key = KeySpace.PartitionOutcome(partition, QueueEnqueueColdProtocol.Root, operation.CommandId);
        var retained = store.Read(view => view.ReadOwnedValue(key))!;
        await QueueClaimVersionExhaustionAssertions.EqualAsync(OrleansSerialization.Deserialize<StoredOutcome>(retained).Result, original);
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        await QueueClaimVersionExhaustionAssertions.EqualAsync(owner.Apply(operation), original);
        await QueueClaimVersionExhaustionAssertions.EqualAsync(owner.ResolveOutcome(operation), original);
        await QueueStoredCapacityAssertions.ImageAsync(store, position, image);
        await Assert.That(store.Read(view => view.ReadOwnedValue(key))).IsEquivalentTo(retained, CollectionOrdering.Matching);
    }
}

using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueScheduledAuthorityOperations
{
    internal static async Task ProcessAsync(DatabaseEngine owner, ZoneTreeStore store, QueueScheduledAuthorityState state,
        Delivery delivery, int terminalVersion)
    {
        await Assert.That(delivery).IsEqualTo(new Delivery(delivery.Id, QueueLifecycleTestProtocol.Payload,
            QueueLifecycleTestProtocol.Headers, delivery.Token, 1,
            state.Flow.Time.AddSeconds(QueueLifecycleTestProtocol.LeaseSeconds), 1, 1));
        var request = new ProcessingRequest(Guid.NewGuid(), state.Flow.Lane, delivery.Token, QueuePendingAuthorityProtocol.Handler, 1,
            [new PutDocument(QueueLifecycleTestProtocol.Collection, delivery.Id, QueueLifecycleTestProtocol.Payload)]);
        var before = store.Position;
        var receipt = state.Flow.Execute(owner, OperationKind.Processing, request, request.CommandId).Get<CommitReceipt>();
        await Assert.That(store.Position).IsEqualTo(before + 1);
        await Assert.That(receipt.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(receipt.Token.Position).IsEqualTo(owner.LastApplied);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(state.Flow.Partition.AtomicPartitionId);
        await Assert.That(receipt.Durability).IsEqualTo(store.Identity.Durability);
        await Assert.That(receipt.Mutations).IsEquivalentTo([
            new MutationReceipt("putDocument", QueueLifecycleTestProtocol.Collection, delivery.Id, 1),
            new MutationReceipt(nameof(DeliveryAction.Ack), state.Flow.Lane.Queue, delivery.Id, terminalVersion)], CollectionOrdering.Matching);
        state.Processed.Add((request, receipt));
    }
}

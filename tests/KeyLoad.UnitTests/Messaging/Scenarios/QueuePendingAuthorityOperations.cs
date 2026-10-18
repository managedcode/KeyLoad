using ManagedCode.Communication.Commands;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueuePendingAuthorityOperations
{
    internal static async Task<QueuePendingAuthorityState> SeedAsync(TestDatabase fixture)
    {
        QueueLifecycleProvisioning.Seed(fixture, byteSublimit: false);
        var flow = new QueueLifecycleTestState(fixture.Partition, QueueWholeFlowStorage.Clock(fixture.Store));
        flow.Batch(fixture.Database, QueueLifecycleHealthy.Enqueue(QueueLifecycleTestProtocol.Parked),
            QueueLifecycleHealthy.Enqueue(QueueLifecycleTestProtocol.Pending),
            QueueLifecycleHealthy.Enqueue(QueuePendingAuthorityProtocol.History)).Get<CommitReceipt>();
        foreach (var id in new[] { QueueLifecycleTestProtocol.Parked, QueueLifecycleTestProtocol.Pending })
        {
            var delivery = await QueueLifecycleOperations.ClaimAsync(fixture.Database, flow, id);
            QueueLifecycleOperations.Complete(fixture.Database, flow, delivery, DeliveryAction.Nack).Get<CommitReceipt>();
        }
        var key = Key(flow, QueueLifecycleTestProtocol.PendingSpace, QueueLifecycleTestProtocol.Pending);
        var marker = fixture.Store.Read(view => view.ReadOwnedValue(key))!;
        var command = new CommandRequest(Guid.NewGuid(), flow.Partition,
            [new ParkPendingQueueMessage(flow.Lane.Queue, QueueLifecycleTestProtocol.Pending, 3, 1)]);
        var operation = fixture.Database.CreateCommand(OperationKind.Batch, command.CommandId, QueueLifecycleTestProtocol.Administrator, flow.Time, JsonSerializer.Serialize(command, JsonDefaults.Options));
        var state = new QueuePendingAuthorityState(flow, operation, key, marker);
        await ProcessAsync(fixture.Database, fixture.Store, state, QueuePendingAuthorityProtocol.History, 1, 1, 3);
        flow.Batch(fixture.Database, new CancelQueueMessage(flow.Lane.Queue, QueueLifecycleTestProtocol.Parked, 3, 1))
            .Get<CommitReceipt>();
        return state;
    }

    internal static void Damage(ZoneTreeStore store, QueuePendingAuthorityState state, QueuePendingAuthorityFault fault)
        => store.Commit((tx, _) =>
        {
            if (fault == QueuePendingAuthorityFault.MissingMarker)
            {
                tx.Delete(state.MarkerKey);
            }
            else
            {
                tx.PutRecord(state.MarkerKey, new QueuePendingDeadLetterReference(QueueLifecycleTestProtocol.Pending, 2,
                QueuePendingAuthorityProtocol.Exhausted));
            }
            return true;
        });

    internal static void Repair(ZoneTreeStore store, QueuePendingAuthorityState state)
        => store.Commit((tx, _) => { tx.Put(state.MarkerKey, state.MarkerBytes); return true; });

    internal static async Task ParkAsync(DatabaseEngine owner, ZoneTreeStore store, QueuePendingAuthorityState state)
    {
        var before = store.Position;
        var result = owner.Apply(state.Park);
        var receipt = result.Get<CommitReceipt>();
        state.Flow.Successes.Add((state.Park, result));
        await Assert.That(store.Position).IsEqualTo(before + 1);
        await Assert.That(receipt.CommandId).IsEqualTo(state.Park.Id);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(state.Flow.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsEqualTo(store.Position);
        await Assert.That(receipt.Durability).IsEqualTo(store.Identity.Durability);
        await Assert.That(receipt.Mutations).IsEquivalentTo([new MutationReceipt(QueuePendingAuthorityProtocol.ParkKind,
            state.Flow.Lane.Queue, QueueLifecycleTestProtocol.Pending, 4)], CollectionOrdering.Matching);
    }

    internal static async Task ContinueAsync(DatabaseEngine owner, ZoneTreeStore store, QueuePendingAuthorityState state)
    {
        state.Flow.Batch(owner, new RedriveQueueMessage(state.Flow.Lane.Queue, QueueLifecycleTestProtocol.Pending, 4, 1))
            .Get<CommitReceipt>();
        await ProcessAsync(owner, store, state, QueueLifecycleTestProtocol.Pending, 3, 2, 7);
        state.Flow.Batch(owner, QueueLifecycleHealthy.Enqueue(QueuePendingAuthorityProtocol.Healthy)).Get<CommitReceipt>();
        await ProcessAsync(owner, store, state, QueuePendingAuthorityProtocol.Healthy, 1, 1, 3);
    }

    private static async Task ProcessAsync(DatabaseEngine owner, ZoneTreeStore store, QueuePendingAuthorityState state,
        string id, int leaseVersion, int generation, int terminalVersion)
    {
        var delivery = await QueueLifecycleOperations.ClaimAsync(owner, state.Flow, id);
        await Assert.That(delivery).IsEqualTo(new Delivery(id, QueueLifecycleTestProtocol.Payload,
            QueueLifecycleTestProtocol.Headers, delivery.Token, leaseVersion,
            state.Flow.Time.AddSeconds(QueueLifecycleTestProtocol.LeaseSeconds), 1, generation));
        await QueuePendingAuthorityLeased.RequireAsync(owner, store, state, delivery, terminalVersion);
        var processing = new ProcessingRequest(Guid.NewGuid(), state.Flow.Lane, delivery.Token,
            QueuePendingAuthorityProtocol.Handler, 1,
            [new PutDocument(QueueLifecycleTestProtocol.Collection, id, QueueLifecycleTestProtocol.Payload)]);
        var result = state.Flow.Execute(owner, OperationKind.Processing, processing, processing.CommandId);
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(processing.CommandId);
        await Assert.That(receipt.Token.Position).IsEqualTo(store.Position);
        await Assert.That(receipt.Mutations).IsEquivalentTo([
            new MutationReceipt(QueuePendingAuthorityProtocol.PutKind, QueueLifecycleTestProtocol.Collection, id, 1),
            new MutationReceipt(nameof(DeliveryAction.Ack), state.Flow.Lane.Queue, id, terminalVersion)], CollectionOrdering.Matching);
        state.Processed.Add(new(id, processing, receipt));
    }

    internal static byte[] Key(QueueLifecycleTestState state, string space, params object[] rest)
        => KeySpace.Partition(space, state.Partition, new object[] { state.Lane.Queue }.Concat(rest).ToArray());
}

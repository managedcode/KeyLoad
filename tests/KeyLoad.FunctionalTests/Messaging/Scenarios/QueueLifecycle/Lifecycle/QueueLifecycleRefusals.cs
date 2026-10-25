using System.Text.Json;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueLifecycleRefusals
{
    internal static async Task InitialAsync(DatabaseEngine database, ZoneTreeStore store, QueueLifecycleTestState state)
    {
        var image = QueueLifecycleImage.Capture(store, state.Lane);
        var full = state.Batch(database, new ParkPendingQueueMessage(state.Lane.Queue, QueueLifecycleTestProtocol.Pending,
            QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.One));
        await Assert.That(Errors.Code(full.Problem).Value).IsEqualTo(ErrorCode.ResourceExhausted);
        await QueueLifecycleImage.SameAsync(store, state.Lane, image);
        var rollback = state.Batch(database,
            new CancelQueueMessage(state.Lane.Queue, QueueLifecycleTestProtocol.Parked, QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.One),
            new ParkPendingQueueMessage(state.Lane.Queue, QueueLifecycleTestProtocol.Pending, QueueLifecycleTestProtocol.Two, QueueLifecycleTestProtocol.One),
            new PutDocument(QueueLifecycleTestProtocol.Collection, "refused", QueueLifecycleTestProtocol.Payload));
        await Assert.That(Errors.Code(rollback.Problem).Value).IsEqualTo(ErrorCode.RevisionConflict);
        await QueueLifecycleImage.SameAsync(store, state.Lane, image);
        await DeniedAndCancelledAsync(database, store, state);
        await Assert.That(database.GetDocument(QueueLifecycleTestProtocol.Administrator, new(state.Partition,
            QueueLifecycleTestProtocol.Collection, "refused"))).IsNull();
    }

    private static async Task DeniedAndCancelledAsync(DatabaseEngine database, ZoneTreeStore store, QueueLifecycleTestState state)
    {
        var image = QueueLifecycleImage.Capture(store, state.Lane);
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, state.Partition, [new CancelQueueMessage(state.Lane.Queue,
            QueueLifecycleTestProtocol.Parked, QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.One)]);
        var denied = state.Execute(database, OperationKind.Batch, command, id, QueueLifecycleTestProtocol.DeniedAdministrator);
        await Assert.That(Errors.Code(denied.Problem).Value).IsEqualTo(ErrorCode.PermissionDenied);
        await QueueLifecycleImage.SameAsync(store, state.Lane, image);
        state.CancelledCommand = command with
        {
            CommandId = Guid.NewGuid()
        };
        var whole = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var cancelled = new CancellationToken(canceled: true);
        var original = database.CreateCommand(OperationKind.Batch, state.CancelledCommand.CommandId, QueueLifecycleTestProtocol.Administrator, default, JsonSerializer.Serialize(state.CancelledCommand, JsonDefaults.Options));
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => database.ApplyEmbedded(original, cancelled));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancelled);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(whole, CollectionOrdering.Matching);
    }

    internal static async Task StaleAsync(DatabaseEngine database, ZoneTreeStore store, QueueLifecycleTestState state, Delivery delivery)
    {
        var image = QueueLifecycleImage.Capture(store, state.Lane);
        var stale = QueueLifecycleOperations.Complete(database, state, delivery, DeliveryAction.Ack);
        await Assert.That(Errors.Code(stale.Problem).Value).IsEqualTo(ErrorCode.StaleLease);
        await QueueLifecycleImage.SameAsync(store, state.Lane, image);
    }
}

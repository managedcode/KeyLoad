using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleAuthorityRefusal
{
    internal static async Task MissingOrderedReferenceAsync(DatabaseEngine database, ZoneTreeStore store, QueueLifecycleTestState state)
    {
        var key = KeySpace.Partition(QueueLifecycleTestProtocol.ParkedSpace, state.Partition, state.Lane.Queue,
            QueueLifecycleTestProtocol.One, QueueLifecycleTestProtocol.Parked);
        var exact = store.Read(view => view.ReadOwnedValue(key))!;
        var original = QueueLifecycleImage.Capture(store, state.Lane);
        store.Commit((transaction, _) => { transaction.Delete(key); return true; });
        var refusal = QueueLifecycleImage.Capture(store, state.Lane);
        var before = store.Position;
        var failed = Assert.ThrowsExactly<KeyLoadException>(() => database.InspectMessage(
            QueueLifecycleTestProtocol.Administrator, state.Lane, QueueLifecycleTestProtocol.Parked));
        await Assert.That(failed.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(store.Position).IsEqualTo(before);
        await QueueLifecycleImage.SameAsync(store, state.Lane, refusal);
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, state.Partition, [new ParkPendingQueueMessage(state.Lane.Queue,
            QueueLifecycleTestProtocol.Pending, QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.One)]);
        var operation = new ReplicatedOperation(id, OperationKind.Batch, QueueLifecycleTestProtocol.Administrator,
            state.Time, JsonSerializer.Serialize(command, JsonDefaults.Options));
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => database.Apply(operation));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(store.Position).IsEqualTo(before);
        await QueueLifecycleImage.SameAsync(store, state.Lane, refusal);
        store.Commit((transaction, _) => { transaction.Put(key, exact); return true; });
        await QueueLifecycleImage.SameAsync(store, state.Lane, original);
        await QueueLifecycleImage.BodyAsync(database, state, QueueLifecycleTestProtocol.Parked, MessageState.DeadLettered,
            QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.One, QueueLifecycleTestProtocol.One);
        await QueueLifecycleAccountingAssertions.InitialAsync(store, state);
    }
}

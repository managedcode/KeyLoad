using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleAuthorityRefusal
{
    internal static async Task OverlappingUsageAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueLifecycleTestState state)
    {
        var key = KeySpace.Partition(QueueLifecycleTestProtocol.CounterSpace, state.Partition, state.Lane.Queue);
        foreach (var bytesOverlap in new[] { false, true })
        {
            var original = store.Read(view => view.ReadOwnedValue(key))!;
            var counters = OrleansSerialization.Deserialize<QueueCounters>(original);
            var exact = QueueWholeFlowStorage.Bytes(store);
            var queue = QueueLifecycleImage.Capture(store, state.Lane);
            var corrupted = bytesOverlap ? counters with
            {
                InFlightBytes = counters.StoredBytes
            }
                : counters with
                {
                    InFlightMessages = QueueLifecycleTestProtocol.Three
                };
            store.Commit((tx, _) => { tx.Put(key, OrleansSerialization.Serialize(corrupted)); return true; });
            var damaged = QueueWholeFlowStorage.Bytes(store);
            var position = store.Position;
            var identity = OrleansSerialization.Serialize(store.Identity);
            var id = Guid.NewGuid();
            var command = new CommandRequest(id, state.Partition,
                [new ParkPendingQueueMessage(state.Lane.Queue, QueueLifecycleTestProtocol.Pending,
                    QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.One)]);
            var operation = database.CreateCommand(OperationKind.Batch, id, QueueLifecycleTestProtocol.Administrator, state.Time, JsonSerializer.Serialize(command, JsonDefaults.Options));
            for (var repeat = 0; repeat < 2; repeat++)
            {
                var refusal = Assert.ThrowsExactly<KeyLoadException>(() => state.SubmitOriginal(database, operation));
                await Assert.That(refusal.Code).IsEqualTo(ErrorCode.RecoveryRequired);
                await Assert.That(refusal.Message).IsEqualTo("The queue lifecycle accounting is invalid.");
                await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, damaged, position, identity);
                await Assert.That(store.Read(view => view.ReadOwnedValue(
                    KeySpace.PartitionOutcome(state.Partition, operation.UserId!, id)))).IsNull();
            }
            store.Commit((tx, _) => { tx.Put(key, original); return true; });
            await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(exact, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            var repaired = state.SubmitOriginal(database, operation);
            await Assert.That(Errors.Code(repaired.Problem)).IsEqualTo(ErrorCode.ResourceExhausted);
            await Assert.That(repaired.Problem?.Detail).IsEqualTo("The queue dead-letter sublimit is exhausted.");

            await Assert.That(repaired.Value).IsNull();
            state.Failures.Add((operation, repaired));
            var receiptImage = QueueWholeFlowStorage.Bytes(store);
            var receiptPosition = store.Position;
            await Assert.That(OrleansSerialization.Serialize(state.SubmitOriginal(database, operation)))
                .IsEquivalentTo(OrleansSerialization.Serialize(repaired), TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, receiptImage, receiptPosition, identity);
            await QueueLifecycleImage.SameAsync(store, state.Lane, queue);
            await QueueLifecycleAccountingAssertions.InitialAsync(store, state);
        }
    }

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
        var operation = database.CreateCommand(OperationKind.Batch, id, QueueLifecycleTestProtocol.Administrator, state.Time, JsonSerializer.Serialize(command, JsonDefaults.Options));
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => state.SubmitOriginal(database, operation));
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

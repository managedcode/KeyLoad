using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Messaging;
using P = KeyLoad.UnitTests.Features.ClusterReplication.CoordinatorShutdownProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class CoordinatorShutdownHealthy
{
    internal static async Task RunAsync(TestDatabase db, CoordinatorShutdownStorage storage, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), db.Partition, [new PutDocument(P.Collection, P.Healthy, P.Json)]);
        var operation = new ReplicatedOperation(command.CommandId, OperationKind.Batch, P.Root,
            TimeProvider.System.GetUtcNow(), System.Text.Encoding.UTF8.GetString(JsonDefaults.Serialize(command)));
        var before = db.Store.Position;
        var originalIdentity = db.Store.Identity;
        var receipt = db.Database.ApplyEmbedded(operation, token).Get<CommitReceipt>();
        var expectedReceipt = new CommitReceipt(command.CommandId,
            new(originalIdentity.Incarnation, db.Partition.AtomicPartitionId, before + P.NextPosition, P.OwnershipEpoch),
            [new(P.Put, P.Collection, P.Healthy, P.FirstRevision)], DurabilityProfile.QuorumProcessDurable);
        await Assert.That(JsonDefaults.Serialize(receipt).SequenceEqual(JsonDefaults.Serialize(expectedReceipt))).IsTrue();
        var image = QueueWholeFlowStorage.Bytes(db.Store);
        var position = db.Store.Position;
        var identity = db.Store.Identity;
        var failures = new List<Exception>();
        storage.CloseOriginal(db, failures);
        for (var cycle = 0; cycle < P.ColdCycles && storage.CanOpen; cycle++)
        {
            var store = storage.Open(db, identity, failures);
            if (store is null)
            {
                break;
            }
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var database = db.CreateConfiguredDatabase(store);
                await Assert.That(store.Identity.NodeId).IsEqualTo(identity.NodeId);
                await Assert.That(store.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                await Assert.That(store.Position).IsEqualTo(position);
                await Assert.That(QueueWholeFlowStorage.Bytes(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
                foreach (var id in new[] { P.Original, P.Healthy })
                {
                    var actual = database.GetDocument(P.Root, new(db.Partition, P.Collection, id), cancellationToken: token);
                    var expected = new DocumentResult(new(db.Partition, P.Collection, id), P.FirstRevision, P.Json, false, []);
                    await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
                }
                var replay = database.ApplyEmbedded(operation, token).Get<CommitReceipt>();
                await Assert.That(JsonDefaults.Serialize(replay).SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
                await Assert.That(store.Position).IsEqualTo(position);
                await Assert.That(QueueWholeFlowStorage.Bytes(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
            }, failures);
            storage.CloseCold(failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

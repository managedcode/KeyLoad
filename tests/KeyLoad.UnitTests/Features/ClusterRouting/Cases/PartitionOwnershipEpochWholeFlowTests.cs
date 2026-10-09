using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Actual native epoch refusals retain failed outcomes before current-authority healthy continuation.</summary>
internal sealed class PartitionOwnershipEpochWholeFlowTests
{
    private const string Resource = "epoch-whole-documents";
    private const string Principal = "root";
    private const string StaleDocument = "stale";
    private const string FutureDocument = "future";
    private const string HealthyDocument = "healthy";
    private const string LiteralJson = "{\"epoch\":true}";
    private const string OutboxFamily = "outbox-head";
    private const long EpochStep = 1;
    private const long FirstRevision = 1;

    [Test]
    public async Task StaleAndFutureNativeEpochsReplayWithoutSecondEffectThenCurrentEpochIsHealthy()
    {
        using var database = new TestDatabase();
        database.Configure(Resource, ResourceKind.Collection);
        var witness = database.Store.Read(view => DatabaseEngine.ReadPlacementWitness(view, database.Partition));
        var outboxKey = KeySpace.Partition(OutboxFamily, database.Partition);
        var originalOutbox = database.Store.Read(view => view.ReadOwnedValue(outboxKey));
        var rejected = new[] { Operation(database, witness.PlacementEpoch - EpochStep, StaleDocument),
            Operation(database, witness.PlacementEpoch + EpochStep, FutureDocument) };
        foreach (var operation in rejected)
        {
            var result = database.Database.Apply(operation);
            await Assert.That(result.Error).IsEqualTo(ErrorCode.OwnershipLost);
            var stored = OutcomeStoreOracle.ReadPartition(database.Store, database.Partition, Principal, operation.Id);
            await Assert.That(stored).IsNotNull();
            await Assert.That(JsonDefaults.Serialize(stored!).AsSpan().SequenceEqual(JsonDefaults.Serialize(result))).IsTrue();
            var position = database.Store.Position;
            await Assert.That(JsonDefaults.Serialize(database.Database.Apply(operation)).AsSpan().SequenceEqual(JsonDefaults.Serialize(result))).IsTrue();
            await Assert.That(database.Store.Position).IsEqualTo(position);
            await RequireNoRejectedEffectsAsync(database, outboxKey, originalOutbox);
        }
        var healthy = Operation(database, witness.PlacementEpoch, HealthyDocument);
        var success = database.Database.Apply(healthy);
        await Assert.That(success.Error).IsNull();
        var receipt = success.Get<CommitReceipt>();
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(witness.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(witness.PlacementEpoch);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(database.Partition.AtomicPartitionId);
        var committed = database.Store.Position;
        await Assert.That(JsonDefaults.Serialize(database.Database.Apply(healthy)).AsSpan().SequenceEqual(JsonDefaults.Serialize(success))).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(committed);
        var reference = new EntityRef(database.Partition, Resource, HealthyDocument);
        var expected = new DocumentResult(reference, FirstRevision, LiteralJson, false, []);
        await Assert.That(JsonDefaults.Serialize(database.Database.GetDocument(Principal, reference)).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    private static ReplicatedOperation Operation(TestDatabase database, long epoch, string document)
    {
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, database.Partition, [new PutDocument(Resource, document, LiteralJson)], epoch);
        return new(id, OperationKind.Batch, Principal, database.Database.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(request, JsonDefaults.Options));
    }

    private static async Task RequireNoRejectedEffectsAsync(TestDatabase database, byte[] outboxKey, byte[]? originalOutbox)
    {
        foreach (var document in new[] { StaleDocument, FutureDocument })
        {
            await Assert.That(database.Store.Read(view => view.GetRecord<DocumentRecord>(
                DocumentStorageKeys.RecordKey(database.Partition, Resource, document)))).IsNull();
        }
        var currentOutbox = database.Store.Read(view => view.ReadOwnedValue(outboxKey));
        await Assert.That(originalOutbox is null && currentOutbox is null || originalOutbox is not null
            && currentOutbox is not null && originalOutbox.AsSpan().SequenceEqual(currentOutbox)).IsTrue();
    }
}

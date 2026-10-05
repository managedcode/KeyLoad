using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PartitionOwnershipEpochTests
{
    private const string Resource = "epoch-documents";

    [Test]
    public async Task CurrentBatchEpochIssuesTokenFromPersistedPlacementWitness()
    {
        using var database = new TestDatabase();
        database.Configure(Resource, ResourceKind.Collection);
        var receipt = database.Commit(new PutDocument(Resource, "epoch-document", "{}"));
        var witness = database.Store.Read(view => DatabaseEngine.ReadPlacementWitness(view, database.Partition));

        await Assert.That(receipt.Token.Incarnation).IsEqualTo(witness.Incarnation);
        await Assert.That(witness.Incarnation).IsEqualTo(database.Store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(database.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(witness.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        database.Store.Read(view =>
        {
            DatabaseEngine.ValidateCommitToken(view, database.Partition, receipt.Token,
                ErrorCode.TokenInvalidated, "The test token is invalid.");
            return true;
        });
    }

    [Test]
    public async Task StaleAndFutureBatchEpochsPersistOwnershipFailureWithoutDomainMutation()
    {
        using var database = new TestDatabase();
        database.Configure(Resource, ResourceKind.Collection);
        var before = database.Store.Position;
        var outboxKey = KeySpace.Partition("outbox-head", database.Partition);
        var outboxBefore = database.Store.Read(view => view.ReadOwnedValue(outboxKey));
        var staleId = Guid.NewGuid();
        var futureId = Guid.NewGuid();
        var stale = Submit(database, staleId, 0);
        var future = Submit(database, futureId, 2);

        await Assert.That(stale.Error).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(future.Error).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(database.Store.Read(view => view.GetRecord<DocumentRecord>(
            DocumentStorageKeys.RecordKey(database.Partition, Resource, "stale")))).IsNull();
        await Assert.That(database.Store.Read(view => view.GetRecord<DocumentRecord>(
            DocumentStorageKeys.RecordKey(database.Partition, Resource, "future")))).IsNull();
        await Assert.That(database.Store.Position).IsGreaterThan(before);
        var outboxAfter = database.Store.Read(view => view.ReadOwnedValue(outboxKey));
        await Assert.That(outboxBefore is null && outboxAfter is null
            || outboxBefore is not null && outboxAfter is not null && outboxBefore.SequenceEqual(outboxAfter)).IsTrue();
        await Assert.That(database.Store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.Outcome("root", staleId))?.ScopeKind))
            .IsEqualTo(CommandOutcomeScopeKind.Partition);
        await Assert.That(database.Store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.Outcome("root", futureId))?.ScopeKind))
            .IsEqualTo(CommandOutcomeScopeKind.Partition);
    }

    [Test]
    public async Task MissingCatalogFailsClosedBeforePartitionCommit()
    {
        using var database = new TestDatabase();
        database.Configure(Resource, ResourceKind.Collection);
        database.Store.Commit((transaction, _) =>
        {
            transaction.Delete(PhysicalShardCatalogRecordSerialization.CatalogKey());
            return true;
        });
        var before = database.Store.Position;
        var commandId = Guid.NewGuid();
        var exception = Assert.ThrowsExactly<KeyLoadException>(() => Submit(database, commandId, 1));

        await Assert.That(exception.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(database.Store.Position).IsEqualTo(before);
        await Assert.That(database.Database.Outcome("root", commandId)).IsNull();
    }

    [Test]
    public async Task CorruptPlacementOwnerTupleFailsBeforeOutcomeOrPartitionMutation()
    {
        using var database = new TestDatabase();
        database.Configure(Resource, ResourceKind.Collection);
        var owner = database.Store.Read(view => PhysicalShardCatalogRecordSerialization.Read(view)!.DefaultShard);
        database.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(AtomicPartitionPlacementSerialization.DirectoryKey(),
                new AtomicPartitionPlacementDirectoryV1(1, 1, 1));
            transaction.PutRecord(AtomicPartitionPlacementSerialization.RowKey(database.Partition),
                new AtomicPartitionPlacementV1(1, database.Partition, owner.PhysicalShardId, 1,
                    owner.Incarnation, owner.VoterIds, owner.PlacementEpoch + 1));
            return true;
        });
        var before = database.Store.Position;
        var commandId = Guid.NewGuid();
        var exception = Assert.ThrowsExactly<KeyLoadException>(() => Submit(database, commandId, 1));

        await Assert.That(exception.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(before);
        await Assert.That(database.Database.Outcome("root", commandId)).IsNull();
        await Assert.That(database.Store.Read(view => view.GetRecord<DocumentRecord>(
            DocumentStorageKeys.RecordKey(database.Partition, Resource, "future")))).IsNull();
    }

    private static OperationResult Submit(TestDatabase database, Guid commandId, long epoch)
    {
        var request = new CommandRequest(commandId, database.Partition,
            [new PutDocument(Resource, epoch == 0 ? "stale" : "future", "{}")], epoch);
        var operation = new ReplicatedOperation(commandId, OperationKind.Batch, "root",
            database.Database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(request, JsonDefaults.Options));
        return database.Database.Apply(operation);
    }
}

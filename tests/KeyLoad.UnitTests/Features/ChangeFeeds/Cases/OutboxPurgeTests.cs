using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed class OutboxPurgeTests
{
    private const string NullJsonPayload = "null";
    private const string ForeignPartitionId = "foreign";

    private static byte[] EntryKey(PartitionRef partition, long sequence)
        => KeySpace.Partition("outbox", partition, sequence);

    [Test]
    public async Task PurgeSubtractsExactUnicodePayloadBytesAndHonorsPinsAndPartialLimits()
    {
        using var database = new TestDatabase();
        database.Configure("orders", ResourceKind.Collection);
        database.Commit(
            new PutDocument("orders", "large-一", "{\"name\":\"雪☃️🌍\",\"text\":\"" + new string('界', 8_000) + "\"}"),
            new PutDocument("orders", "second", "{\"name\":\"é" + new string('ø', 512) + "\"}"));

        var original = database.Database.GetOutboxStatus("root", database.Partition).Head;
        var firstPayload = database.Store.Read(view => view.ReadOwnedValue(EntryKey(database.Partition, 1))!);
        var consumer = new ProjectionConsumerRef(database.Partition, "pin");
        var configureId = Guid.NewGuid();
        database.Submit(OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(configureId, consumer, new(1, [], [])), id: configureId);
        var deniedId = Guid.NewGuid();
        await Assert.That(database.Submit(OperationKind.PurgeOutbox,
            new PurgeOutboxRequest(deniedId, database.Partition, 1), id: deniedId).Error).IsEqualTo(ErrorCode.Conflict);

        var releaseId = Guid.NewGuid();
        database.Submit(OperationKind.ReleaseProjectionConsumer,
            new ReleaseProjectionConsumerRequest(releaseId, consumer, 1), id: releaseId);
        var purgeId = Guid.NewGuid();
        var partial = database.Submit(OperationKind.PurgeOutbox,
            new PurgeOutboxRequest(purgeId, database.Partition, 2, Limit: 1), id: purgeId).Get<OutboxHead>();

        await Assert.That(partial.FirstAvailable).IsEqualTo(2);
        await Assert.That(partial.StoredRecords).IsEqualTo(original.StoredRecords - 1);
        await Assert.That(partial.StoredBytes).IsEqualTo(original.StoredBytes - firstPayload.Length);
        var next = database.Database.GetOutboxStatus("root", database.Partition).Head;
        await Assert.That(next.StoredBytes).IsEqualTo(partial.StoredBytes);
    }

    [Test]
    public async Task CorruptOutboxPointReadRollsBackPurgeAndAllowsHealthySubsequentWrite()
    {
        using var database = new TestDatabase();
        database.Configure("orders", ResourceKind.Collection);
        database.Commit(new PutDocument("orders", "first", "{}"));
        var entryKey = EntryKey(database.Partition, 1);
        var bytes = database.Store.Read(view => view.ReadOwnedValue(entryKey)!);
        var entry = NativeSerialization.Deserialize<OutboxEntry>(bytes);
        var wrongPartition = entry with { Commit = entry.Commit with { AtomicPartitionId = ForeignPartitionId } };
        database.Store.Commit((transaction, _) =>
        {
            transaction.Put(entryKey, NativeSerialization.Serialize(wrongPartition));
            return 0;
        });
        var before = database.Database.GetOutboxStatus("root", database.Partition).Head;

        var purgeId = Guid.NewGuid();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => database.Submit(OperationKind.PurgeOutbox,
            new PurgeOutboxRequest(purgeId, database.Partition, 1), id: purgeId)).Code).IsEqualTo(ErrorCode.Corruption);
        var after = database.Database.GetOutboxStatus("root", database.Partition).Head;
        await Assert.That(after).IsEqualTo(before);

        database.Commit(new PutDocument("orders", "healthy", "{}"));
        await Assert.That(database.Database.GetOutboxStatus("root", database.Partition).Head.Tail).IsEqualTo(2);
    }

    [Test]
    public async Task MissingAndWrongSequencePointsCannotPartiallyPurge()
    {
        await AssertCorruptPointRollback(missing: true);
        await AssertCorruptPointRollback(missing: false);
    }

    [Test]
    public async Task PartialPurgeCountersSurviveReopenAndAcceptAnotherProducer()
    {
        using var database = new TestDatabase();
        database.Configure("orders", ResourceKind.Collection);
        database.Commit(new PutDocument("orders", "one", "{\"v\":\"" + new string('語', 4_000) + "\"}"),
            new PutDocument("orders", "two", "{}"));
        var purgeId = Guid.NewGuid();
        var purged = database.Submit(OperationKind.PurgeOutbox,
            new PurgeOutboxRequest(purgeId, database.Partition, 1), id: purgeId).Get<OutboxHead>();

        database.Store.Dispose();
        using var reopenedStore = new ZoneTreeStore(new(database.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var reopenedDatabase = new DatabaseEngine(reopenedStore, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.TimeSeriesExecution());
        var recovered = reopenedDatabase.GetOutboxStatus("root", database.Partition).Head;
        await Assert.That(recovered).IsEqualTo(purged);
        var commandId = Guid.NewGuid();
        var payload = System.Text.Json.JsonSerializer.Serialize(new CommandRequest(commandId, database.Partition,
            [new PutDocument("orders", "three", "{}")]), JsonDefaults.Options);
        var outcome = reopenedDatabase.Apply(new(commandId, OperationKind.Batch, "root", TimeProvider.System.GetUtcNow(), payload));
        await Assert.That(outcome.Error).IsNull();
        await Assert.That(reopenedDatabase.GetOutboxStatus("root", database.Partition).Head.Tail).IsEqualTo(3);
    }

    [Test]
    public async Task PurgeDoesNotReadEntriesBeyondRequestedCutAndNextEntryRemainsConsumable()
    {
        using var database = new TestDatabase();
        database.Configure("orders", ResourceKind.Collection);
        database.Commit(new PutDocument("orders", "one", "{}"),
            new PutDocument("orders", "two", "{\"text\":\"" + new string('雪', 16_000) + "\"}"));
        var corrupt = CorruptFirstAndNextEntries(database);

        var initialHead = database.Database.GetOutboxStatus("root", database.Partition).Head;
        await AssertNoOpPurge(database, initialHead, through: 0);

        RestoreEntry(database, corrupt.FirstKey, corrupt.FirstEntry);
        var purgeId = Guid.NewGuid();
        var purged = database.Submit(OperationKind.PurgeOutbox,
            new PurgeOutboxRequest(purgeId, database.Partition, 1), id: purgeId).Get<OutboxHead>();
        await Assert.That(purged.FirstAvailable).IsEqualTo(2);
        await Assert.That(purged.StoredRecords).IsEqualTo(1);

        await AssertNoOpPurge(database, purged, through: 1);

        RestoreEntry(database, corrupt.NextKey, corrupt.NextEntry);
        var consumer = new ProjectionConsumerRef(database.Partition, "after-purge");
        var configureId = Guid.NewGuid();
        database.Submit(OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(configureId, consumer, new(1, [], [])), id: configureId);
        var batch = database.Database.ReadProjectionBatch("root", new(consumer));
        await Assert.That(batch.Entries.Select(entry => entry.Sequence)).IsEquivalentTo(new long[] { 2 },
            CollectionOrdering.Matching);
    }

    private static (byte[] FirstKey, byte[] FirstEntry, byte[] NextKey, byte[] NextEntry)
        CorruptFirstAndNextEntries(TestDatabase database)
    {
        var firstKey = EntryKey(database.Partition, 1);
        var firstEntry = database.Store.Read(view => view.ReadOwnedValue(firstKey)!);
        var nextKey = EntryKey(database.Partition, 2);
        var nextEntry = database.Store.Read(view => view.ReadOwnedValue(nextKey)!);
        database.Store.Commit((transaction, _) =>
        {
            transaction.Put(firstKey, System.Text.Encoding.UTF8.GetBytes(NullJsonPayload));
            transaction.Put(nextKey, System.Text.Encoding.UTF8.GetBytes(NullJsonPayload));
            return 0;
        });
        return (firstKey, firstEntry, nextKey, nextEntry);
    }

    private static async Task AssertNoOpPurge(TestDatabase database, OutboxHead expectedHead, long through)
    {
        var commandId = Guid.NewGuid();
        var positionBeforeNoOp = database.Store.Position;
        var actual = database.Submit(OperationKind.PurgeOutbox,
            new PurgeOutboxRequest(commandId, database.Partition, through), id: commandId).Get<OutboxHead>();
        await Assert.That(actual).IsEqualTo(expectedHead);
        await Assert.That(database.Store.Position).IsEqualTo(positionBeforeNoOp + 1);
        await Assert.That(OutcomeStoreOracle.ReadPartition(database.Store, database.Partition, "root", commandId)).IsNotNull();
    }

    private static void RestoreEntry(TestDatabase database, byte[] key, byte[] value)
        => database.Store.Commit((transaction, _) =>
        {
            transaction.Put(key, value);
            return 0;
        });

    private static async Task AssertCorruptPointRollback(bool missing)
    {
        using var database = new TestDatabase();
        database.Configure("orders", ResourceKind.Collection);
        database.Commit(new PutDocument("orders", "one", "{}"), new PutDocument("orders", "two", "{}"));
        var key = EntryKey(database.Partition, 1);
        if (missing)
        {
            database.Store.Commit((transaction, _) =>
            {
                transaction.Delete(key);
                return 0;
            });
        }
        else
        {
            var bytes = database.Store.Read(view => view.ReadOwnedValue(key)!);
            var entry = NativeSerialization.Deserialize<OutboxEntry>(bytes) with { Sequence = 3 };
            database.Store.Commit((transaction, _) =>
            {
                transaction.Put(key, NativeSerialization.Serialize(entry));
                return 0;
            });
        }

        var before = database.Database.GetOutboxStatus("root", database.Partition).Head;
        var purgeId = Guid.NewGuid();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => database.Submit(OperationKind.PurgeOutbox,
            new PurgeOutboxRequest(purgeId, database.Partition, 2), id: purgeId)).Code).IsEqualTo(ErrorCode.Corruption);
        var after = database.Database.GetOutboxStatus("root", database.Partition).Head;
        await Assert.That(after).IsEqualTo(before);
    }
}

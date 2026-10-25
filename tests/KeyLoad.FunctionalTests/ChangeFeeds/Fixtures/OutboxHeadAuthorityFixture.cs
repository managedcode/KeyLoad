using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal static class OutboxHeadAuthorityFixture
{
    internal const string Root = "root";
    internal const string Collection = "orders";
    internal const string HeadSpace = "outbox-head";
    internal const string EntrySpace = "outbox";
    internal const string MissingRow = "The complete outbox operation requires its original committed row.";
    internal const string HeadDetail = "The committed outbox head does not match its canonical retained range.";
    internal const string BytesDetail = "The committed outbox byte accounting does not match the reclaimed entry.";

    internal static Command<DatabaseCommand> Operation<T>(DatabaseEngine owner, OperationKind kind, Guid id, T request)
        => owner.CreateCommand(kind, id, Root, owner.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(request));

    internal static Command<DatabaseCommand> Write(DatabaseEngine owner, PartitionRef partition, string id)
    {
        var command = Guid.NewGuid();
        return Operation(owner, OperationKind.Batch, command,
            new CommandRequest(command, partition, [new PutDocument(Collection, id, "{\"n\":3}", 0L)]));
    }

    internal static async Task<OutboxHeadAuthorityState> SeedAsync(TestDatabase database, CancellationToken token)
    {
        database.Configure(Collection, ResourceKind.Collection);
        await OutboxHeadAuthorityAssertions.ExactAsync(database.Database.GetOutboxStatus(Root, database.Partition).Head,
            new OutboxHead(0L, 1L, 0L, 0L));
        var id = Guid.NewGuid();
        var seed = Operation(database.Database, OperationKind.Batch, id, new CommandRequest(id, database.Partition,
            [new PutDocument(Collection, "a", "{\"n\":1}", 0L), new PutDocument(Collection, "b", "{\"n\":2}", 0L)]));
        var seedResult = database.Database.ApplyEmbedded(seed, token);
        var receipt = seedResult.Get<CommitReceipt>();
        await OutboxHeadAuthorityAssertions.ReceiptAsync(seed, receipt,
            new("putDocument", Collection, "a", 1L), new("putDocument", Collection, "b", 1L));
        var key = KeySpace.Partition(HeadSpace, database.Partition);
        var original = database.Store.Read(view => view.ReadOwnedValue(key)) ?? throw new InvalidOperationException(MissingRow);
        var first = database.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition(EntrySpace, database.Partition, 1L)))
            ?? throw new InvalidOperationException(MissingRow);
        var second = database.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition(EntrySpace, database.Partition, 2L)))
            ?? throw new InvalidOperationException(MissingRow);
        await OutboxHeadAuthorityAssertions.ExactAsync(database.Database.GetOutboxStatus(Root, database.Partition).Head,
            new OutboxHead(2L, 1L, 2L, first.LongLength + second.LongLength));
        await Assert.That(database.Database.GetDocument(Root, new(database.Partition, Collection, "c"), cancellationToken: token)).IsNull();
        var purgeId = Guid.NewGuid();
        var purge = Operation(database.Database, OperationKind.PurgeOutbox, purgeId, new PurgeOutboxRequest(purgeId, database.Partition, 2L));
        return new(database, database.Partition, seed, receipt, seedResult, purge, key, original, first.LongLength + second.LongLength, first.LongLength);
    }

    internal static void Damage(TestDatabase database, OutboxHeadAuthorityState state, int fault)
    {
        var valid = new OutboxHead(2L, 1L, 2L, state.StoredBytes);
        var broken = fault switch
        {
            0 => valid with { StoredBytes = -1L },
            1 => valid with { StoredRecords = 1L },
            2 => valid with { FirstAvailable = 0L },
            3 => valid with { StoredBytes = 0L },
            4 => valid with { StoredBytes = state.FirstBytes - 1L },
            5 => valid with { StoredBytes = state.StoredBytes + 1L },
            6 => valid with { Tail = -1L },
            7 => valid with { FirstAvailable = 4L },
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        database.Store.Commit((transaction, _) => { transaction.PutRecord(state.Key, broken); return true; });
    }
}

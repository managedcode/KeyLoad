using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Query;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class FeedVertexAuthorityFixture
{
    internal const string Collection = "orders";
    internal const string Root = "root";
    internal const string ForeignId = "foreign-current-feed-row";
    internal const string MissingRow = "The complete flow requires its original canonical document.";
    internal const string PutKind = "putDocument";
    internal const string OutboxSpace = "outbox";
    private const string ForeignHistoryOwner = "foreign-history-owner";
    internal const string ForeignCollection = "foreign-history-collection";

    internal static async Task<FeedVertexAuthorityState> SeedAsync(TestDatabase database, bool historical = false)
    {
        database.Configure(Collection, ResourceKind.Collection);
        var query = new AstQueryRequest(database.Partition,
            new(Collection, null, [new("*", "*")], null, [], 10), AllowFullScan: true);
        var token = TestContext.Current!.Execution.CancellationToken;
        var initialCut = database.Store.Position;
        var feed = database.Database.ReadChangeFeed(Root, new(database.Partition, Collection), token);
        var live = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).StartLiveQuery(Root, new(query), cancellationToken: token);
        await Assert.That(feed.Changes).IsEmpty();
        await Assert.That(feed.ThroughSequence).IsEqualTo(0L);
        await Assert.That(feed.Tail).IsEqualTo(0L);
        await Assert.That(feed.FirstAvailable).IsEqualTo(1L);
        await Assert.That(feed.HasMore).IsFalse();
        await Assert.That(feed.CutPosition).IsEqualTo(initialCut);
        await Assert.That(live.Rows).IsEmpty();
        await Assert.That(live.ThroughSequence).IsEqualTo(0L);
        await Assert.That(live.Cursor).IsNotNull();
        await Assert.That(live.CutPosition).IsEqualTo(initialCut);
        var command = new CommandRequest(Guid.NewGuid(), database.Partition,
            [new PutDocument(Collection, "a", "{\"n\":1}", 0L), new PutDocument(Collection, "b", "{\"n\":2}", 0L)]);
        var time = new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
        var receipt = database.Submit(OperationKind.Batch, command, id: command.CommandId, time: time).Get<CommitReceipt>();
        CommandRequest? update = null;
        CommitReceipt? updateReceipt = null;
        if (historical)
        {
            update = new(Guid.NewGuid(), database.Partition, [new PutDocument(Collection, "b", "{\"n\":22}", 1L, ExplicitReplacement: true)]);
            updateReceipt = database.Submit(OperationKind.Batch, update, id: update.CommandId, time: time).Get<CommitReceipt>();
        }
        var key = historical ? global::KeyLoad.Core.KeySpace.Partition(OutboxSpace, database.Partition, 3L)
            : DocumentStorageKeys.RecordKey(new(database.Partition, Collection, "b"));
        var original = database.Store.Read(view => view.ReadOwnedValue(key)) ?? throw new InvalidOperationException(MissingRow);
        return new(command, receipt, time, query, feed.Cursor, live.Cursor, key, original, update, updateReceipt);
    }

    internal static void Damage(TestDatabase database, FeedVertexAuthorityState state, int? historicalFault = null)
    {
        if (historicalFault is { } selected)
        {
            var entry = NativeSerialization.Deserialize<OutboxEntry>(state.Original) ?? throw new InvalidOperationException(MissingRow);
            var after = entry.After ?? throw new InvalidOperationException(MissingRow);
            var damaged = selected switch
            {
                0 => entry with
                {
                    After = after with
                    {
                        Reference = after.Reference with
                        { Partition = new("foreign-tenant", "foreign-database", "orders", ForeignHistoryOwner) }
                    }
                },
                1 => entry with { After = after with { Reference = after.Reference with { Collection = ForeignCollection } } },
                2 => entry with { After = after with { Reference = after.Reference with { Id = ForeignId } } },
                3 => entry with
                {
                    Before = (entry.Before ?? throw new InvalidOperationException(MissingRow)) with
                    { Reference = after.Reference with { Id = ForeignId } }
                },
                _ => throw new ArgumentOutOfRangeException(nameof(historicalFault))
            };
            database.Store.Commit((transaction, _) => { transaction.PutRecord(state.Key, damaged); return true; });
            return;
        }
        var row = NativeSerialization.Deserialize<DocumentRecord>(state.Original) ?? throw new InvalidOperationException(MissingRow);
        database.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(state.Key, row with { Reference = row.Reference with { Id = ForeignId } });
            return true;
        });
    }
}

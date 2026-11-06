using System.Collections.Immutable;
using System.Globalization;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.AnalyticsStore;

internal sealed class AnalyticsTimestampSeekTests
{
    private const string Store = "timed-events";
    private const string Metric = "amount";
    private const string First = "a";
    private const string Last = "z";
    private const string Upper = "upper";
    private static readonly DateTimeOffset From = new(1969, 12, 31, 23, 59, 59, TimeSpan.Zero);
    private static readonly DateTimeOffset Until = From.AddSeconds(2);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task NarrowRangesSeekOnlyTheirEventsAndKeepTimestampMovesDeletesAndColdReads(bool transactional)
    {
        using var db = new TestDatabase();
        var definition = new ResourceDefinition(Store, ResourceKind.AnalyticsStore, db.Partition.TransactionDomainId)
        {
            Transactional = transactional,
            AnalyticsSchema = new([], [Metric])
        };
        db.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(db.Partition.TenantId, db.Partition.DatabaseId, definition)).Get<ResourceDefinition>();
        var outside = Enumerable.Range(0, 512).Select(index => Event(
            "outside-" + index.ToString("D4", CultureInfo.InvariantCulture), From.AddTicks(index - 1024)));
        AnalyticsEventData[] rows = [.. outside, Event(Last, From), Event(First, From.AddTicks(1)), Event(Upper, Until)];
        await CommitAsync(db, new UpsertAnalyticsEvents(Store, [.. rows],
            rows.ToImmutableDictionary(row => row.Id, _ => 0L, StringComparer.Ordinal)));
        var before = db.Store.GetReadDiagnostics();
        var snapshot = db.Database.CaptureAnalyticsAggregate("root", Query(db.Partition));
        var visited = db.Store.GetReadDiagnostics().RangeBaselineEntries - before.RangeBaselineEntries;
        await Assert.That(snapshot.Rows.Select(row => row.Id)).IsEquivalentTo([First, Last], CollectionOrdering.Matching);
        await Assert.That(snapshot.Rows.All(row => row.Revision == 1 && row.Metrics[Metric] == 3)).IsTrue();
        await Assert.That(visited).IsLessThan(8L);
        await ReadAsync(db.Database, db.Partition, From, Until, [First, Last]);
        await CommitAsync(db, new UpsertAnalyticsEvents(Store, [Event(Last, Until.AddTicks(1))],
            ImmutableDictionary<string, long>.Empty.Add(Last, 1)));
        await CommitAsync(db, new DeleteAnalyticsEvents(Store, ImmutableDictionary<string, long>.Empty.Add(First, 1)));
        await ReadAsync(db.Database, db.Partition, From, Until, []);
        await ReadAsync(db.Database, db.Partition, Until, Until.AddTicks(2), [Upper, Last]);
        await CommitAsync(db, new UpsertAnalyticsEvents(Store, [Event(First, From)],
            ImmutableDictionary<string, long>.Empty.Add(First, 2)));
        await ReadAsync(db.Database, db.Partition, From, Until, [First]);
        var identity = db.Store.Identity;
        var cut = db.Store.Position;
        db.JoinOwnedResources();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var cold = ZoneTreeExistingStore.Open(new(db.Directory) { Incarnation = identity.Incarnation },
                identity.NodeId, db.StorageExecution);
            var database = db.CreateConfiguredDatabase(cold);
            await ReadAsync(database, db.Partition, From, Until, [First]);
            await ReadAsync(database, db.Partition, Until, Until.AddTicks(2), [Upper, Last]);
            await Assert.That(database.CaptureAnalyticsAggregate("root", Query(db.Partition)).Rows.Single().Revision).IsEqualTo(3L);
            cut = await NativeStoreAssertions.ReopenedAsync(cold, cut);
        }
    }

    private static AnalyticsEventData Event(string id, DateTimeOffset timestamp)
        => new(id, timestamp, ImmutableDictionary<string, string>.Empty,
            ImmutableDictionary<string, double>.Empty.Add(Metric, 3), null, []);

    private static async Task CommitAsync(TestDatabase db, Mutation mutation)
    {
        var id = Guid.NewGuid();
        var result = await new EmbeddedCoordinator(db.Database).RuntimeSubmitAsync<CommitReceipt>(OperationKind.Batch,
            id, "root", new CommandRequest(id, db.Partition, [mutation]),
            cancellationToken: TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.IsSuccess).IsTrue();
    }

    private static AggregateAnalyticsRequest Query(PartitionRef partition)
        => new(partition, Store, From, Until, [], [new("count", string.Empty, AnalyticsAggregateKind.Count)], 1);

    private static async Task ReadAsync(DatabaseEngine database, PartitionRef partition, DateTimeOffset from,
        DateTimeOffset until, string[] expected)
    {
        var result = database.ReadAnalyticsEvents("root", new(partition, Store, from, until, 10, null));
        await Assert.That(result.Events.Select(row => row.Id)).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(result.Events.All(row => row.Timestamp >= from && row.Timestamp < until
            && row.Metrics[Metric] == 3)).IsTrue();
        await Assert.That(result.HasMore).IsFalse();
    }
}

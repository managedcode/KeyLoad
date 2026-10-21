using System.Collections.Immutable;
using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreTestData;
using O = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreTestOperations;
using P = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreTestProjection;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal sealed class AnalyticsTimestampRangeTests
{
    [Test]
    [Arguments(-2, 0)]
    [Arguments(-2, 1)]
    [Arguments(2, 0)]
    [Arguments(2, 1)]
    public async Task CanonicalTimestampRangesPreserveExactRowsAndCompleteAggregates(int days, int minutes)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var db = new TestDatabase();
        await O.InitializeAsync(db, false);
        var from = new DateTimeOffset(TimeProvider.System.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero).AddDays(days);
        var until = from.AddHours(1);
        var row = D.Event(db, D.First, D.Alpha, 2) with
        {
            Timestamp = from.AddMinutes(minutes)
        };
        await O.CommitAsync(db, new UpsertAnalyticsEvents(D.Store, [row], O.Revisions((row.Id, 0))),
            AnalyticsStoreProtocol.UpsertKind, row.Id, 1, token);
        var expected = new AnalyticsEventRecord(row.Id, row.Timestamp, row.Dimensions, row.Metrics, row.RowAccess,
            [.. row.Links.Select(link => AnalyticsExpectedResourceCreation.Bind(db, link,
                AnalyticsExpectedResourceCreation.Anchor))], 1);
        var read = new ReadAnalyticsEventsRequest(db.Partition, D.Store, from, until, 10, null);
        await O.EqualAsync(new AnalyticsEventsPage([expected], false, null),
            db.Database.ReadAnalyticsEvents(D.Root, read, token));
        var query = D.Query(db) with
        {
            FromInclusive = from,
            ToExclusive = until
        };
        var snapshot = db.Database.CaptureAnalyticsAggregate(D.Root, query, token);
        await O.EqualAsync(ImmutableArray.Create(expected), snapshot.Rows);
        await P.RunAsync(snapshot, new([D.Row(D.Alpha, 1, 2, 2, 2, 2)], 1), token);
        db.Database.ValidateAnalyticsAggregatePublication(snapshot);
        await O.EqualAsync(new AnalyticsEventsPage([expected], false, null),
            db.Database.ReadAnalyticsEvents(D.Root, read, token));
    }
}

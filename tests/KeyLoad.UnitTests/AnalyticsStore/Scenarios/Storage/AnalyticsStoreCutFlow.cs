using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreTestData;
using O = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreTestOperations;
using P = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreTestProjection;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsStoreCutFlow
{
    private const string UpsertKind = "upsertAnalyticsEvents";
    internal static async Task RunAsync(CancellationToken token)
    {
        using var db = new TestDatabase();
        await O.InitializeAsync(db, false);
        var initial = D.Event(db, D.First, D.Alpha, 2);
        await O.CommitAsync(db, new UpsertAnalyticsEvents(D.Store, [initial], O.Revisions((D.First, 0))), UpsertKind, D.First, 1, token);
        var snapshot = db.Database.CaptureAnalyticsAggregate(D.Root, D.Query(db), token);
        var changed = D.Event(db, D.First, D.Alpha, 10);
        await O.CommitAsync(db, new UpsertAnalyticsEvents(D.Store, [changed], O.Revisions((D.First, 1))), UpsertKind, D.First, 2, token);
        await P.RunAsync(snapshot, new([D.Row(D.Alpha, 1, 2, 2, 2, 2)], 1), token);
        var stale = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
        {
            db.Database.ValidateAnalyticsAggregatePublication(snapshot);
            return Task.CompletedTask;
        });
        ArgumentNullException.ThrowIfNull(stale);
        await Assert.That(stale.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await P.AggregateAsync(db, D.Root, new([D.Row(D.Alpha, 1, 10, 10, 10, 10)], 1), token);
        var before = db.Database.GetOutboxStatus(D.Root, db.Partition);
        var malformed = D.Query(db) with
        {
            Metrics = [new(D.Sum, null!, AnalyticsAggregateKind.Sum)]
        };
        var rejected = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Task.FromResult(db.Database.CaptureAnalyticsAggregate(D.Root, malformed, token)));
        ArgumentNullException.ThrowIfNull(rejected);
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Validation);
        await O.EqualAsync(before, db.Database.GetOutboxStatus(D.Root, db.Partition));
        await O.RowsAsync(db, D.Root, [changed], [2], false, token);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Task.FromResult(db.Database.CaptureAnalyticsAggregate(D.Root, D.Query(db), canceled.Token)));
        await O.EqualAsync(before, db.Database.GetOutboxStatus(D.Root, db.Partition));
        await P.AggregateAsync(db, D.Root, new([D.Row(D.Alpha, 1, 10, 10, 10, 10)], 1), token);
    }
}

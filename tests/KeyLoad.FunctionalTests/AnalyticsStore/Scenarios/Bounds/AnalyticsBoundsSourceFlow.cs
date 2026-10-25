using D = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsStoreTestData;
using N = KeyLoad.FunctionalTests.AnalyticsStore.RuntimeAnalyticsBounds;
using O = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsStoreTestOperations;
using P = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsBoundsData;

namespace KeyLoad.FunctionalTests.AnalyticsStore;

internal static class AnalyticsBoundsSourceFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var db = new TestDatabase();
        var events = Enumerable.Range(0, P.SourceRows).Select(index => D.Event(db, P.Id(index), D.Alpha, 1)).ToArray();
        await AnalyticsBoundsOwner.SeedAsync(db, events);
        var query = P.Query(db);
        var original = db.Database.CaptureAnalyticsAggregate(D.Root, query, token);
        var bytes = checked(original.Work.ReadBytes - 1);
        await Assert.That(original.Rows.Length).IsEqualTo(P.SourceRows);
        var scanBound = AnalyticsBoundsOwner.ReadEngine(db, new()
        {
        });
        var byteBound = AnalyticsBoundsOwner.ReadEngine(db, new()
        {
        });
        foreach (var engine in new[] { scanBound, byteBound })
        {
            await N.UnchangedAsync(db, async () =>
            {
                await N.RefusedAsync(() => N.Query(engine, query, token));
                await O.EqualAsync(new AnalyticsAggregateResult([P.Row(D.Alpha, 1, 7)], 1),
                    N.Query(engine, P.Query(db, P.HealthyStore), token));
            });
        }
    }
}

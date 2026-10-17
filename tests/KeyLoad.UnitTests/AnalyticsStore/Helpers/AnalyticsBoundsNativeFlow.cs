using ZoneTree.Analytics;
using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestData;
using N = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsBoundsNative;
using O = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestOperations;
using P = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsBoundsData;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsBoundsNativeFlow
{
    internal static async Task GroupsAsync(CancellationToken token)
    {
        using var db = new TestDatabase(nativeReplicaAdmission: true);
        await AnalyticsBoundsOwner.SeedAsync(db, [D.Event(db, D.First, D.Alpha, 2), D.Event(db, D.Second, D.Beta, 4)]);
        await N.UnchangedAsync(db, async () =>
        {
            var error = await N.RefusedAsync(() => N.QueryAsync(db.Database, P.Query(db) with { MaxGroups = 1 }, token));
            await Assert.That(error.InnerException is AnalyticsQueryException).IsTrue();
            await O.EqualAsync(new AnalyticsAggregateResult([P.Row(D.Alpha, 1, 2), P.Row(D.Beta, 1, 4)], 2),
                await N.QueryAsync(db.Database, P.Query(db), token));
        });
    }

    internal static async Task SumAsync(CancellationToken token)
    {
        using var db = new TestDatabase(nativeReplicaAdmission: true);
        await AnalyticsBoundsOwner.SeedAsync(db,
            [D.Event(db, D.First, D.Alpha, double.MaxValue), D.Event(db, D.Second, D.Alpha, double.MaxValue)]);
        await N.UnchangedAsync(db, async () =>
        {
            await N.RefusedAsync(() => N.QueryAsync(db.Database, P.Query(db), token));
            var count = P.Query(db) with
            {
                Metrics = [new(D.Count, string.Empty, AnalyticsAggregateKind.Count)]
            };
            var expected = new AnalyticsAggregateResult([new(
                System.Collections.Immutable.ImmutableDictionary<string, string>.Empty.Add(D.Kind, D.Alpha),
                System.Collections.Immutable.ImmutableDictionary<string, double>.Empty.Add(D.Count, 2))], 2);
            await O.EqualAsync(expected, await N.QueryAsync(db.Database, count, token));
        });
    }

    internal static async Task ResultAsync(CancellationToken token)
    {
        using var db = new TestDatabase(nativeReplicaAdmission: true);
        await AnalyticsBoundsOwner.SeedAsync(db, [D.Event(db, D.First, D.Alpha, 2), D.Event(db, D.Second, D.Beta, 4)]);
        var expected = new AnalyticsAggregateResult([P.Row(D.Alpha, 1, 2), P.Row(D.Beta, 1, 4)], 2);
        var bound = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(expected, JsonDefaults.Options).Length - 1;
        await N.UnchangedAsync(db, async () =>
        {
            await N.RefusedAsync(() => N.QueryAsync(db.Database, P.Query(db), token, bound));
            await O.EqualAsync(new AnalyticsAggregateResult([P.Row(D.Alpha, 1, 7)], 1),
                await N.QueryAsync(db.Database, P.Query(db, P.HealthyStore), token, bound));
        });
    }
}

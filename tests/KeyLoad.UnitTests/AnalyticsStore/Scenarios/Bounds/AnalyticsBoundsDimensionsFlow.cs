using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestData;
using N = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsBoundsNative;
using O = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestOperations;
using P = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsBoundsData;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsBoundsDimensionsFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var db = new TestDatabase();
        string[] values = [string.Empty, P.Padded, P.Plain, P.Composed, P.Decomposed, P.Control];
        var events = values.Select((value, index) => D.Event(db, P.Id(index), value, index + 1)).ToArray();
        await AnalyticsBoundsOwner.SeedAsync(db, events);
        await RequireAsync(db, events, [1, 1, 1, 1, 1, 1], token);
        events[1] = D.Event(db, P.Id(1), P.Changed, 20);
        await AnalyticsBoundsOwner.PutAsync(db, D.Store, [events[1]], 2, 1);
        await RequireAsync(db, events, [1, 2, 1, 1, 1, 1], token);
        await O.CommitAsync(db, new DeleteAnalyticsEvents(D.Store, O.Revisions((events[0].Id, 1))),
            P.DeleteKind, events[0].Id, 3, token);
        await RequireAsync(db, events.Skip(1).ToArray(), [2, 1, 1, 1, 1], token);
    }

    private static Task RequireAsync(TestDatabase db, AnalyticsEventData[] events, long[] revisions, CancellationToken token)
        => N.UnchangedAsync(db, async () =>
        {
            await O.RowsAsync(db, D.Root, events.OrderBy(row => row.Id, StringComparer.Ordinal).ToArray(), revisions, false, token);
            var expected = events.OrderBy(row => row.Dimensions[D.Kind], StringComparer.Ordinal)
                .Select(row => P.Row(row.Dimensions[D.Kind], 1, row.Metrics[D.Value])).ToArray();
            await O.EqualAsync(new AnalyticsAggregateResult([.. expected], events.Length),
                N.Query(db.Database, P.Query(db), token));
        });
}

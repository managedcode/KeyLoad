using KeyLoad.Core;
using KeyLoad.Server;
using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestData;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsStoreNativeTestProjection
{
    private const string Prefix = "keyload-analytics-projection-unit-";
    internal static async Task AggregateAsync(TestDatabase db, string principal, AnalyticsAggregateResult expected,
        CancellationToken token)
    {
        var snapshot = db.Database.CaptureAnalyticsAggregate(principal, D.Query(db), token);
        await RunAsync(snapshot, expected, token);
        db.Database.ValidateAnalyticsAggregatePublication(snapshot);
    }

    internal static async Task RunAsync(AnalyticsAggregateSnapshot snapshot, AnalyticsAggregateResult expected,
        CancellationToken token)
    {
        var path = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
        AnalyticsStoreNativeGeneration? generation = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            generation = new(snapshot, path);
            await AnalyticsStoreNativeTestOperations.EqualAsync(expected, generation.Query(token));
        }, failures);
        if (generation is not null)
        {
            ServerFailureObserver.Observe(generation.Dispose, failures);
            if (generation.HasJoinedEngine && Directory.Exists(path))
            {
                await ServerFailureObserver.ObserveAsync(() => { Directory.Delete(path, recursive: true); return Task.CompletedTask; }, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

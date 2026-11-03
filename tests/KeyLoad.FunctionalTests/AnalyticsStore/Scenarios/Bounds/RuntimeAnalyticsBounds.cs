using KeyLoad.FunctionalTests.Authorization;
using KeyLoad.Server;
using D = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsStoreTestData;
using O = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsStoreTestOperations;

namespace KeyLoad.FunctionalTests.AnalyticsStore;

internal static class RuntimeAnalyticsBounds
{
    private const string Prefix = "keyload-analytics-bound-unit-";
    internal static async Task<AnalyticsAggregateResult> QueryAsync(DatabaseEngine database,
        AggregateAnalyticsRequest request, CancellationToken token)
    {
        var snapshot = database.CaptureAnalyticsAggregate(D.Root, request, token);
        var path = Path.Combine(AppContext.BaseDirectory, Prefix + Guid.NewGuid().ToString("N"));
        AnalyticsStoreGeneration? generation = null;
        AnalyticsAggregateResult? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            generation = new(snapshot, path);
            result = await generation.QueryAsync(token);
        }, failures);
        if (generation is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => generation.DisposeAsync().AsTask(), failures);
            if (generation.HasJoinedEngine && Directory.Exists(path))
            {
                ServerFailureObserver.Observe(() => Directory.Delete(path, true), failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        database.ValidateAnalyticsAggregatePublication(snapshot);
        return result ?? throw new InvalidOperationException("The complete analytics result is missing.");
    }

    internal static async Task UnchangedAsync(TestDatabase db, Func<Task> body)
    {
        var identity = OrleansSerialization.Serialize(db.Store.Identity);
        var image = TeamRowPolicyAssertions.Image(db.Store);
        var position = db.Store.Position;
        var journal = db.ReadCanonicalCut();
        var applied = db.Database.LastApplied;
        var outbox = db.Database.GetOutboxStatus(D.Root, db.Partition);
        await body();
        await Assert.That(OrleansSerialization.Serialize(db.Store.Identity).SequenceEqual(identity)).IsTrue();
        await Assert.That(TeamRowPolicyAssertions.Image(db.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await Assert.That(db.ReadCanonicalCut()).IsEqualTo(journal);
        await Assert.That(db.Database.LastApplied).IsEqualTo(applied);
        await O.EqualAsync(outbox, db.Database.GetOutboxStatus(D.Root, db.Partition));
    }

    internal static async Task<KeyLoadException> RefusedAsync(Func<Task<AnalyticsAggregateResult>> query)
    {
        AnalyticsAggregateResult? published = null;
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => published = await query());
        ArgumentNullException.ThrowIfNull(error);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(published).IsNull();
        return error;
    }
}

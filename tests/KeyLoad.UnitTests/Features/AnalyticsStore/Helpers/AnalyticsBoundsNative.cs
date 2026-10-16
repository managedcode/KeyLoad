using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Authorization;
using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestData;
using O = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestOperations;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsBoundsNative
{
    private const string Prefix = "keyload-analytics-bound-unit-";
    internal static async Task<AnalyticsAggregateResult> QueryAsync(DatabaseEngine database,
        AggregateAnalyticsRequest request, CancellationToken token, int? resultBytes = null)
    {
        var snapshot = database.CaptureAnalyticsAggregate(D.Root, request, token);
        if (resultBytes is { } maximum)
        {
            snapshot.Work.ConstrainResultBytes(maximum);
        }

        var path = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
        AnalyticsStoreNativeGeneration? generation = null;
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
        var identity = NativeSerialization.Serialize(db.Store.Identity);
        var image = TeamRowPolicyAssertions.Image(db.Store);
        var position = db.Store.Position;
        var journal = db.ReadJournalCut();
        var applied = db.Database.LastApplied;
        var outbox = db.Database.GetOutboxStatus(D.Root, db.Partition);
        await body();
        await Assert.That(NativeSerialization.Serialize(db.Store.Identity).SequenceEqual(identity)).IsTrue();
        await Assert.That(TeamRowPolicyAssertions.Image(db.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await Assert.That(db.ReadJournalCut()).IsEqualTo(journal);
        await Assert.That(db.Database.LastApplied).IsEqualTo(applied);
        await O.EqualAsync(outbox, db.Database.GetOutboxStatus(D.Root, db.Partition));
    }

    internal static async Task<KeyLoadException> RefusedAsync(Func<Task<AnalyticsAggregateResult>> query)
    {
        AnalyticsAggregateResult? published = null;
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => { published = await query(); });
        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(published).IsNull();
        return error;
    }
}

using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class CatalogArrayPathLifetime
{
    internal static async Task ObserveAsync(ZoneTreeStore store, Func<Task> operation)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => ConserveAsync(store, image, cut), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ConserveAsync(ZoneTreeStore store, string[] image, long cut)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(TeamRowPolicyAssertions.Image(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue(), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(store.Position).IsEqualTo(cut), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ColdAsync(TestDatabase db, CommandRequest original, CommitReceipt originalReceipt,
        byte[] originalOutcome, CommandRequest patch, CommitReceipt patchReceipt, byte[] patchOutcome, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(db.Store);
        var cut = db.Store.Position;
        var identity = OrleansSerialization.Serialize(db.Store.Identity);
        db.Store.Dispose();
        for (var cycle = 0; cycle < 2; cycle++)
        {
            ZoneTreeStore? cold = null;
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                cold = new(new(db.Directory), db.StorageExecution, UnitExecutionOptions.PointCacheExecution(),
                    timeProvider: db.Database.EvaluationClock);
                await ServerFailureObserver.ObserveAsync(async () =>
                    await Assert.That(OrleansSerialization.Serialize(cold.Identity).AsSpan().SequenceEqual(identity)).IsTrue(), failures);
                await ServerFailureObserver.ObserveAsync(() => ConserveAsync(cold, image, cut), failures);
                var database = db.CreateConfiguredDatabase(cold);
                await ServerFailureObserver.ObserveAsync(() => CatalogArrayPathAssertions.CheckAsync(db, database, cold,
                    db.Partition, true, true, 2L, token), failures);
                await ServerFailureObserver.ObserveAsync(() => CatalogArrayPathAssertions.ReplayAsync(database, cold,
                    original, originalReceipt, originalOutcome, token), failures);
                await ServerFailureObserver.ObserveAsync(() => CatalogArrayPathAssertions.ReplayAsync(database, cold,
                    patch, patchReceipt, patchOutcome, token), failures);
            }, failures);
            if (cold is not null)
            {
                await ServerFailureObserver.ObserveAsync(() => ConserveAsync(cold, image, cut), failures);
            }
            await ServerFailureObserver.ObserveAsync(() =>
            {
                cold?.Dispose();
                return Task.CompletedTask;
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }
}

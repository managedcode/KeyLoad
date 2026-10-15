using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class ModelRootPolicyLifetime
{
    internal static async Task ObserveAsync(ZoneTreeStore store, Func<Task> operation)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => ConserveAsync(store, image, cut, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ConserveAsync(ZoneTreeStore store, string[] image, long cut, byte[] identity)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(TeamRowPolicyAssertions.Image(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue(), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(store.Position).IsEqualTo(cut), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(NativeSerialization.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ColdAsync(ModelRootPolicyFixture fixture, CancellationToken token)
    {
        var db = fixture.Harness;
        var image = TeamRowPolicyAssertions.Image(db.Store);
        var cut = db.Store.Position;
        var identity = NativeSerialization.Serialize(db.Store.Identity);
        db.Store.Dispose();
        var failures = new List<Exception>();
        for (var cycle = 0; cycle < 2; cycle++)
        {
            ZoneTreeStore? cold = null;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                cold = new(new(db.Directory), db.StorageExecution, UnitExecutionOptions.PointCacheExecution(),
                    timeProvider: db.Database.EvaluationClock);
                await ServerFailureObserver.ObserveAsync(() => ConserveAsync(cold, image, cut, identity), failures);
                var database = db.CreateConfiguredDatabase(cold);
                await ServerFailureObserver.ObserveAsync(() => ModelRootPolicyAssertions.WaveAsync(fixture, database, cold, true, true, token), failures);
                await ServerFailureObserver.ObserveAsync(() => ModelRootPolicyFlow.ReplayAsync(fixture, database, cold, token), failures);
                await ServerFailureObserver.ObserveAsync(() => ConserveAsync(cold, image, cut, identity), failures);
            }, failures);
            ServerFailureObserver.Observe(() => cold?.Dispose(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

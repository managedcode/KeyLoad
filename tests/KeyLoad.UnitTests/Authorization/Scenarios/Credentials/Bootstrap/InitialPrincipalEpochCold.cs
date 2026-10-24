using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Authorization;

internal static class InitialPrincipalEpochCold
{
    internal static async Task RunAsync(InitialPrincipalEpochFixture fixture, InitialPrincipalEpochState state,
        Command<DatabaseCommand> refused, CancellationToken token)
    {
        var db = fixture.Harness;
        var image = InitialPrincipalEpochAssertions.Image(db.Store);
        var position = db.Store.Position;
        var identity = db.Store.Identity;
        var bytes = OrleansSerialization.Serialize(identity);
        db.JoinOwnedResources();
        var failures = new List<Exception>();
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var store = ZoneTreeExistingStore.Open(new(db.Directory)
                {
                    Incarnation = identity.Incarnation
                },
                    identity.NodeId, db.StorageExecution);
                await ServerFailureObserver.ObserveAsync(() => InitialPrincipalEpochHealthy.RequireAsync(fixture,
                    db.CreateConfiguredDatabase(store), store, state, refused, token), failures);
                await ServerFailureObserver.ObserveAsync(() => InitialPrincipalEpochAssertions.ConserveAsync(store,
                    image, position, bytes), failures);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

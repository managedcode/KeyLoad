using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class CapabilityOutputCold
{
    internal static async Task RunAsync(TestDatabase fixture,
        (CommandRequest Command, CommitReceipt Receipt)[] seeds, int mode, CancellationToken token)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(fixture.Store);
        var position = fixture.Store.Position;
        var identity = fixture.Store.Identity;
        var identityBytes = NativeSerialization.Serialize(identity);
        fixture.JoinOwnedResources();
        var failures = new List<Exception>();
        for (var cold = 0; cold < 2; cold++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var store = ZoneTreeExistingStore.Open(new(fixture.Directory)
                {
                    Incarnation = identity.Incarnation
                },
                    identity.NodeId, fixture.StorageExecution);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var owner = fixture.CreateConfiguredDatabase(store);
                    var engine = new QueryEngine(owner, UnitExecutionOptions.QueryExecution(CapabilityOutputFixture.Options(mode)));
                    var checks = new List<Exception>();
                    await ServerFailureObserver.ObserveAsync(() => CapabilityOutputOperations.RunAsync(store,
                        fixture.Partition, engine, mode, token), checks);
                    await ServerFailureObserver.ObserveAsync(() => CapabilityOutputOperations.ReplayAsync(owner, seeds), checks);
                    await ServerFailureObserver.ObserveAsync(() => CapabilityOutputOperations.ConserveAsync(store,
                        image, position, identityBytes), checks);
                    ServerFailureObserver.ThrowIfAny(checks);
                }, failures);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

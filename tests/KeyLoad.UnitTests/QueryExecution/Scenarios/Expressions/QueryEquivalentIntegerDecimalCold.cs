using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.UnitTests.QueryExecution.QueryEquivalentIntegerDecimalProtocol;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class QueryEquivalentIntegerDecimalCold
{
    internal static async Task RunAsync(TestDatabase fixture, CommandRequest seed, CommitReceipt receipt,
        CancellationToken token)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(fixture.Store);
        var cut = fixture.Store.Position;
        var identity = fixture.Store.Identity;
        var identityBytes = OrleansSerialization.Serialize(identity);
        fixture.JoinOwnedResources();
        var failures = new List<Exception>();
        for (var cold = 0; cold < ColdCuts; cold++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var store = ZoneTreeExistingStore.Open(new(fixture.Directory)
                {
                    Incarnation = identity.Incarnation
                }, identity.NodeId, fixture.StorageExecution);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var owner = fixture.CreateConfiguredDatabase(store);
                    var checks = new List<Exception>();
                    await ServerFailureObserver.ObserveAsync(() => CapabilityOutputOperations.ConserveAsync(store,
                        image, cut, identityBytes), checks);
                    await ServerFailureObserver.ObserveAsync(() => QueryEquivalentIntegerDecimalPages.RunAsync(
                        new QueryEngine(owner, UnitExecutionOptions.QueryExecution()), store, fixture.Partition, null, token), checks);
                    await ServerFailureObserver.ObserveAsync(() => QueryEquivalentIntegerDecimalCurrent.ReplayAsync(owner,
                        store, seed, receipt, token), checks);
                    await ServerFailureObserver.ObserveAsync(() => CapabilityOutputOperations.ConserveAsync(store,
                        image, cut, identityBytes), checks);
                    ServerFailureObserver.ThrowIfAny(checks);
                }, failures);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

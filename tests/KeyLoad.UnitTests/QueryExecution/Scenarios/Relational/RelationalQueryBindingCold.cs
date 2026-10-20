using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class RelationalQueryBindingCold
{
    private const int ColdOpens = 2;

    internal static async Task RunAsync(TestDatabase fixture, CommandRequest seed, CommitReceipt receipt, CancellationToken token)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(fixture.Store);
        var cut = fixture.Store.Position;
        var identity = fixture.Store.Identity;
        var identityBytes = OrleansSerialization.Serialize(identity);
        fixture.JoinOwnedResources();
        var failures = new List<Exception>();
        for (var cold = 0; cold < ColdOpens; cold++)
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
                    var engine = new QueryEngine(owner, UnitExecutionOptions.QueryExecution());
                    var checks = new List<Exception>();
                    await ServerFailureObserver.ObserveAsync(() => CapabilityOutputOperations.ConserveAsync(store, image, cut, identityBytes), checks);
                    await ServerFailureObserver.ObserveAsync(() => RelationalQueryBindingAssertions.RecordAsync(fixture, owner, fixture.Partition, token), checks);
                    await ServerFailureObserver.ObserveAsync(() => RelationalQueryBindingAssertions.InvalidAsync(engine, store, fixture.Partition, token), checks);
                    await ServerFailureObserver.ObserveAsync(() => RelationalQueryBindingAssertions.HealthyAsync(engine, store, fixture.Partition, token), checks);
                    await ServerFailureObserver.ObserveAsync(() => BinderFieldLineageAuthority.ReplayAsync(owner, store, seed, receipt, token), checks);
                    await ServerFailureObserver.ObserveAsync(() => CapabilityOutputOperations.ConserveAsync(store, image, cut, identityBytes), checks);
                    ServerFailureObserver.ThrowIfAny(checks);
                }, failures);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

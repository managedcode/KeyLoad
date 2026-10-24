using KeyLoad.Core.Authorization;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Messaging;

internal static class SubscriptionOldCutCold
{
    internal static DatabaseEngine Open(ZoneTreeStore store, SubscriptionOldCutState state)
        => new(store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(),
            UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(),
            UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.RuntimeClaimsExecution(),
            UnitExecutionOptions.TimeSeriesExecution(), state.Clock);

    internal static async Task RunAsync(string path, SubscriptionOldCutState state, string[] bytes,
        long position, StoreIdentity identity, CancellationToken token)
    {
        var failures = new List<Exception>();
        for (var cold = 0; cold < 2; cold++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                ZoneTreeStore? store = null;
                var cycle = new List<Exception>();
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    store = new(new(path), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
                    var database = Open(store, state);
                    await SubscriptionOldCutAssertions.ConservedAsync(store, bytes, position, identity);
                    await ServerFailureObserver.ObserveAsync(() => SubscriptionOldCutHealthy.FinalAsync(database, state), cycle);
                    await ServerFailureObserver.ObserveAsync(() => SubscriptionOldCutReplay.ProcessingAsync(database, store, state), cycle);
                    await ServerFailureObserver.ObserveAsync(() => SubscriptionOldCutSavedFailure.RetainedAsync(database, store, state), cycle);
                }, cycle);
                if (store is not null)
                {
                    await ServerFailureObserver.ObserveAsync(() => SubscriptionOldCutAssertions.ConservedAsync(store,
                        bytes, position, identity), cycle);
                    ServerFailureObserver.Observe(store.Dispose, cycle);
                }
                ServerFailureObserver.ThrowIfAny(cycle);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.GraphTraversal;

namespace KeyLoad.UnitTests.Features.Search;

internal static class GraphSearchLineageCold
{
    internal static async Task RunAsync(GraphSearchLineageFixture state, string[] image, long position, StoreIdentity identity)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = ZoneTreeExistingStore.Open(new(state.Harness.Directory)
            {
                Incarnation = identity.Incarnation
            }, identity.NodeId, state.Harness.StorageExecution);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                state.Store = store;
                state.Owner = state.Harness.CreateConfiguredDatabase(store);
                await GraphLabelUseOperations.ConserveAsync(store, image, position, NativeSerialization.Serialize(identity));
                await GraphSearchLineageValues.HealthyAsync(state);
                await GraphSearchLineageValues.ReplayAsync(state);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

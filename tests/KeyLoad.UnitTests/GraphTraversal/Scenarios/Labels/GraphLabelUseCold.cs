using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphLabelUseCold
{
    internal static async Task RunAsync(TestDatabase fixture, CommandRequest seed, CommitReceipt receipt, byte[] outcome, CancellationToken token)
    {
        var image = CanonicalStoreImage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = fixture.Store.Identity;
        var bytes = OrleansSerialization.Serialize(identity);
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
                    await GraphLabelUseOperations.ReadAsync(owner, store, fixture.Partition, true, token);
                    await GraphLabelUseOperations.ReplayAsync(owner, store, seed, receipt, outcome);
                }, failures);
                await ServerFailureObserver.ObserveAsync(() => GraphLabelUseOperations.ConserveAsync(store,
                    image, position, bytes), failures);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

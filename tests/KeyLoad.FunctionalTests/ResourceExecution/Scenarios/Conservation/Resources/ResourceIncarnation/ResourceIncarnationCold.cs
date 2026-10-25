using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.FunctionalTests.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal static class ResourceIncarnationCold
{
    internal static async Task RunAsync(TestDatabase fixture, ResourceIncarnationState state, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = fixture.Store.Identity;
        fixture.JoinOwnedResources();
        for (var wave = 0; wave < 2; wave++)
        {
            token.ThrowIfCancellationRequested();
            await OpenAsync(fixture, identity, async store =>
            {
                await ResourceIncarnationAssertions.UnchangedAsync(store, image, position);
                var database = fixture.CreateConfiguredDatabase(store);
                await ResourceIncarnationAssertions.PreserveAsync(store, fixture.Partition, state);
                await ResourceIncarnationAssertions.ReplayAsync(database, store, state);
                await ResourceIncarnationAssertions.RowAsync(database, fixture.Partition, state.Lifetime, wave + 2);
                await ResourceIncarnationAssertions.WriteAsync(database, fixture.Partition, state.Lifetime, wave + 3);
                await ResourceIncarnationAssertions.PreserveAsync(store, fixture.Partition, state);
                image = QueueWholeFlowStorage.Bytes(store);
                position = store.Position;
            });
        }
    }

    internal static async Task OpenAsync(TestDatabase fixture, StoreIdentity identity, Func<ZoneTreeStore, Task> operation)
    {
        var failures = new List<Exception>();
        var lifetime = new StorageFailureOwnerLifetime();
        var store = lifetime.Acquire(() => ZoneTreeExistingStore.Open(
            new(fixture.Directory)
            {
                Incarnation = identity.Incarnation
            }, identity.NodeId, fixture.StorageExecution), failures);
        if (store is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(store.Identity.NodeId).IsEqualTo(identity.NodeId);
                await Assert.That(store.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                await operation(store);
            }, failures);
            lifetime.Close(store, failures);
            foreach (var failure in failures.ToArray())
            {
                ServerFailureObserver.Observe(() =>
                {
                    failure.Data[ResourceIncarnationLiteral.ColdOwnerKey] = store;
                    failure.Data[ResourceIncarnationLiteral.ColdRootKey] = fixture.Directory;
                    failure.Data[ResourceIncarnationLiteral.ColdCloseKnownKey] = lifetime.CanAdvance;
                    failure.Data[ResourceIncarnationLiteral.OwnerLifetimeKey] = lifetime;
                }, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

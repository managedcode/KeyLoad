using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Messaging;

namespace KeyLoad.UnitTests.ResourceExecution;

internal static class AtomicTriadStagedCold
{
    internal static async Task RunAsync(AtomicTriadStagedFixture state, StoreIdentity identity, string[] bytes, long position)
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
                state.Owner = null!;
                ServerFailureObserver.Observe(() => state.Owner = state.Harness.CreateConfiguredDatabase(store), failures);
                await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedCurrent.EqualAsync(store.Identity, identity), failures);
                await ServerFailureObserver.ObserveAsync(async () => await Assert.That(store.Position).IsEqualTo(position), failures);
                await ServerFailureObserver.ObserveAsync(async () => await Assert.That(QueueWholeFlowStorage.Bytes(store))
                    .IsEquivalentTo(bytes, TUnit.Assertions.Enums.CollectionOrdering.Matching), failures);
                await ServerFailureObserver.ObserveAsync(async () => await Assert.That(OrleansSerialization.Serialize(store.Identity)
                    .AsSpan().SequenceEqual(OrleansSerialization.Serialize(identity))).IsTrue(), failures);
                await ServerFailureObserver.ObserveAsync(() => state.Owner is null ? throw new InvalidOperationException(AtomicTriadStagedProtocol.Missing)
                    : AtomicTriadStagedCurrent.RequireProducedAsync(state), failures);
                await ServerFailureObserver.ObserveAsync(() => state.Owner is null ? throw new InvalidOperationException(AtomicTriadStagedProtocol.Missing)
                    : AtomicTriadStagedReplay.RunAsync(state, true), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

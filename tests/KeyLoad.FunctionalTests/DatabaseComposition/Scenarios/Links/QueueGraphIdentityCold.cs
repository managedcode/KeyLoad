using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.DatabaseComposition;

internal static class QueueGraphIdentityCold
{
    internal static async Task RunAsync(QueueGraphIdentityState state, bool dirty, bool repair = false)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            var identity = state.Fixture.Store.Identity;
            using var store = ZoneTreeExistingStore.Open(
                new(state.Fixture.Directory)
                {
                    Incarnation = identity.Incarnation
                },
                identity.NodeId, state.Fixture.StorageExecution);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(JsonDefaults.Serialize(store.Identity).AsSpan()
                    .SequenceEqual(JsonDefaults.Serialize(identity))).IsTrue();
                await Assert.That(store.Position).IsEqualTo(state.ColdPosition);
                await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(state.ColdImage,
                    global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
                var engine = state.Fixture.CreateConfiguredDatabase(store);
                if (dirty)
                {
                    await QueueGraphIdentityAssertions.RefusesAsync(state, engine, store);
                    if (!repair)
                    {
                        return;
                    }
                    store.Commit((tx, _) => { tx.Put(state.EndpointKey, state.OriginalEndpoint); return true; });
                    await Assert.That(store.Read(view => view.ReadOwnedValue(state.EndpointKey))!
                        .AsSpan().SequenceEqual(state.OriginalEndpoint)).IsTrue();
                    await QueueGraphIdentityAssertions.ModelsAsync(state, engine, store, healthy: false);
                    await QueueGraphIdentityAssertions.HealthyAsync(state, engine, store);
                    state.ColdImage = QueueWholeFlowStorage.Bytes(store);
                    state.ColdPosition = store.Position;
                    return;
                }
                var before = QueueWholeFlowStorage.Bytes(store);
                var position = store.Position;
                await QueueGraphIdentityAssertions.ModelsAsync(state, engine, store, healthy: true);
                var receipt = engine.Apply(state.Healthy).Get<CommitReceipt>();
                var outcome = engine.ResolveOutcome(state.Healthy).Get<CommitReceipt>();
                await Assert.That(OrleansSerialization.Serialize(receipt).AsSpan()
                    .SequenceEqual(state.HealthyReceipt)).IsTrue();
                await Assert.That(OrleansSerialization.Serialize(outcome).AsSpan()
                    .SequenceEqual(state.HealthyReceipt)).IsTrue();
                var seed = engine.Apply(state.Seed).Get<CommitReceipt>();
                await Assert.That(OrleansSerialization.Serialize(seed).AsSpan()
                    .SequenceEqual(OrleansSerialization.Serialize(state.SeedReceipt))).IsTrue();
                await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(before,
                    global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
                await Assert.That(store.Position).IsEqualTo(position);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}

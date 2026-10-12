using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal static class QueueGraphReplayCold
{
    internal static async Task RunAsync(QueueGraphReplayState state, bool dirty, bool repair = false)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            var source = state.Source;
            var identity = source.Fixture.Store.Identity;
            using var store = ZoneTreeExistingStore.Open(new(source.Fixture.Directory)
            { Incarnation = identity.Incarnation }, identity.NodeId, source.Fixture.StorageExecution);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(JsonDefaults.Serialize(store.Identity).AsSpan()
                    .SequenceEqual(JsonDefaults.Serialize(identity))).IsTrue();
                await Assert.That(store.Position).IsEqualTo(source.ColdPosition);
                await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(source.ColdImage,
                    global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
                var engine = source.Fixture.CreateConfiguredDatabase(store);
                if (dirty)
                {
                    await QueueGraphReplayAssertions.RefusesAsync(state, engine, store);
                    if (!repair)
                    { return; }
                    store.Commit((tx, _) => { tx.Put(source.EndpointKey, source.OriginalEndpoint); return true; });
                    await Assert.That(store.Read(view => view.ReadOwnedValue(source.EndpointKey))!
                        .AsSpan().SequenceEqual(source.OriginalEndpoint)).IsTrue();
                    await QueueGraphReplayAssertions.ReplaysAsync(state, engine, store, continued: false);
                    await QueueGraphReplayAssertions.ContinueAsync(state, engine, store);
                    source.ColdImage = QueueWholeFlowStorage.Bytes(store);
                    source.ColdPosition = store.Position;
                    return;
                }
                await QueueGraphReplayAssertions.ReplaysAsync(state, engine, store, continued: true);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}

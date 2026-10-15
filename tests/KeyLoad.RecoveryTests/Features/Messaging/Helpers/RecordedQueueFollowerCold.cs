namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class RecordedQueueFollowerCold
{
    internal static async Task RunAsync(PeerDiscoveryNativeCluster cluster, IReadOnlyList<RecordedQueueFollowerOutcome> retained,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var originals = cluster.Nodes.ToDictionary(node => node.Configuration.LocalId, node => new
        {
            Canonical = RecordedQueueFollowerNative.Image(node.Database.Store),
            Replica = RecordedQueueFollowerNative.Image(node.ReplicaStore),
            Identity = NativeSerialization.Serialize(node.Database.Store.Identity),
            ReplicaIdentity = NativeSerialization.Serialize(node.ReplicaStore.Identity),
            CanonicalPosition = node.Database.Store.Position,
            ReplicaPosition = node.ReplicaStore.Position,
            Cut = node.Materializer.AppliedPosition
        });
        await cluster.CloseForColdAsync();
        token.ThrowIfCancellationRequested();
        cluster.ReopenAllAfterCold();
        foreach (var node in cluster.Nodes)
        {
            var original = originals[node.Configuration.LocalId];
            await node.Materializer.WaitForApplyAsync(original.Cut, token);
            await RecordedQueueFollowerAssertions.EqualAsync(node.Materializer.AppliedPosition, original.Cut);
            await RecordedQueueFollowerAssertions.EqualAsync(node.Database.Store.Position, original.CanonicalPosition);
            await RecordedQueueFollowerAssertions.EqualAsync(node.ReplicaStore.Position, original.ReplicaPosition);
            await RecordedQueueFollowerAssertions.EqualAsync(NativeSerialization.Serialize(node.Database.Store.Identity), original.Identity);
            await RecordedQueueFollowerAssertions.EqualAsync(NativeSerialization.Serialize(node.ReplicaStore.Identity), original.ReplicaIdentity);
            await Assert.That(RecordedQueueFollowerNative.Image(node.Database.Store))
                .IsEquivalentTo(original.Canonical, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(RecordedQueueFollowerNative.Image(node.ReplicaStore))
                .IsEquivalentTo(original.Replica, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            foreach (var outcome in retained)
            {
                await RecordedQueueFollowerAssertions.EqualAsync(node.Log.ReadEntry(outcome.Entry.Index), outcome.Entry);
                await RecordedQueueFollowerAssertions.EqualAsync(OutcomeStoreOracle.Read(node.Database.Store,
                    outcome.Entry.Operation!), outcome.Result);
            }
        }
        await RecordedQueueFollowerNative.ImagesAgreeAsync(cluster);
        await RecordedQueueFollowerTerminal.RequireAsync(cluster);
    }
}

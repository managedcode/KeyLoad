using KeyLoad.Storage;
using static KeyLoad.RecoveryTests.Features.Messaging.RecordedQueueFollowerProtocol;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class RecordedQueueFollowerNative
{
    internal static async Task<PeerDiscoveryNativeNode> LeaderAsync(PeerDiscoveryNativeCluster cluster, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            foreach (var node in cluster.Nodes)
            {
                if (await node.Consensus.IsLeaderAsync(token))
                {
                    return node;
                }
            }
            await Task.Delay(RecoveryExecutionOptions.Configuration(cluster.Sender.Configuration).Value.HeartbeatInterval,
                TimeProvider.System, token);
        }
    }

    internal static async Task<RecordedQueueFollowerOutcome> SubmitAsync<T>(PeerDiscoveryNativeCluster cluster,
        PeerDiscoveryNativeNode leader, OperationKind kind, T request, Guid id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var index = checked(leader.Log.State.LastIndex + NextIndexStep);
        var placement = leader.Database.ReadAtomicPartitionPlacement(PeerDiscoveryNativeClockProfile.PrincipalId,
            Partition);
        var expected = new CommitToken(placement.Incarnation, Partition.AtomicPartitionId, index, placement.PlacementEpoch);
        var original = leader.Database.CreateNativeOperation(kind, id, PeerDiscoveryNativeClockProfile.PrincipalId,
            default, NativeSerialization.Serialize(request));
        var before = TimeProvider.System.GetUtcNow();
        var result = await leader.Consensus.SubmitAsync(original, token);
        var after = TimeProvider.System.GetUtcNow();
        var entry = leader.Log.ReadEntry(index) ?? throw new InvalidOperationException("The original committed native entry is missing.");
        await Assert.That(entry.Operation!.Id).IsEqualTo(id);
        await Assert.That(entry.Operation.Kind).IsEqualTo(kind);
        await Assert.That(entry.Operation.PrincipalId).IsEqualTo(PeerDiscoveryNativeClockProfile.PrincipalId);
        await Assert.That(entry.Operation.EvaluatedAt >= before && entry.Operation.EvaluatedAt <= after).IsTrue();
        foreach (var node in cluster.Nodes)
        {
            await node.Materializer.WaitForApplyAsync(index, token);
            await RecordedQueueFollowerAssertions.EqualAsync(node.Log.ReadEntry(index), entry);
            await RecordedQueueFollowerAssertions.EqualAsync(OutcomeStoreOracle.Read(node.Database.Store, entry.Operation), result);
        }
        await ImagesAgreeAsync(cluster);
        return new(entry, result, expected);
    }

    internal static async Task ImagesAgreeAsync(PeerDiscoveryNativeCluster cluster)
    {
        var expected = Image(cluster.Sender.Database.Store);
        foreach (var node in cluster.Nodes)
        {
            await Assert.That(Image(node.Database.Store)).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }
    }

    internal static string[] Image(IAtomicStore store) => store.Read(view =>
    {
        var page = view.Scan([], ImageRecords);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The native follower whole-store image exceeded the declared fixture bound.");
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
    });

    internal static async Task UntilAsync(DateTimeOffset deadline, CancellationToken token)
    {
        var remaining = deadline - TimeProvider.System.GetUtcNow();
        while (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, TimeProvider.System, token);
            remaining = deadline - TimeProvider.System.GetUtcNow();
        }
    }
}

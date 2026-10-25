using KeyLoad.FunctionalTests.Messaging;
using P = KeyLoad.FunctionalTests.ResourceExecution.CachePolicyEpochProtocol;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal static class CachePolicyEpochCold
{
    internal static async Task RunAsync(CachePolicyEpochState state, CancellationToken token)
    {
        var identity = state.Store.Identity;
        var signingKey = identity.SigningKey.ToArray();
        var image = QueueWholeFlowStorage.Bytes(state.Store);
        var position = state.Store.Position;
        for (var cycle = 0; cycle < P.ColdCount; cycle++)
        {
            state.Owner.Close();
            await CachePolicyEpochAssertions.EqualAsync(new CacheMemorySnapshot(0L, 0, 0, false,
                P.PoolBytes, P.PoolEntries), state.Owner.Budget.GetSnapshot());
            state.Owner.Open(first: false);
            var current = state.Store.Identity;
            await Assert.That(current.NodeId).IsEqualTo(identity.NodeId);
            await Assert.That(current.Incarnation).IsEqualTo(identity.Incarnation);
            await Assert.That(current.ReadGeneration).IsGreaterThanOrEqualTo(identity.ReadGeneration);
            await Assert.That(current.SigningKey.Span.SequenceEqual(signingKey)).IsTrue();
            await Assert.That(current.FormatVersion).IsEqualTo(identity.FormatVersion);
            await Assert.That(current.KeyCodecVersion).IsEqualTo(identity.KeyCodecVersion);
            await Assert.That(current.Durability).IsEqualTo(identity.Durability);
            await Assert.That(current.DispatchPaused).IsEqualTo(identity.DispatchPaused);
            await Assert.That(current.MinimumReaderContract).IsEqualTo(identity.MinimumReaderContract);
            var cold = state.Owner.OpeningCache;
            await Assert.That(cold.Hits).IsEqualTo(0L);
            await CachePolicyEpochAssertions.UnchangedAsync(state, image, position);
            await CachePolicyEpochAssertions.ReadAsync(state, true, true, token);
            await CachePolicyEpochRefusals.CancelAsync(state);
            await CachePolicyEpochRefusals.ReplayAsync(state, token);
        }
    }
}

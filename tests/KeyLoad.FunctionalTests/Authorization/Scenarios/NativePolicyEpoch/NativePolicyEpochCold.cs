using KeyLoad.FunctionalTests.Messaging;
using P = KeyLoad.FunctionalTests.Authorization.NativePolicyEpochProtocol;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class NativePolicyEpochCold
{
    internal static async Task RunAsync(NativePolicyEpochState state, CancellationToken token)
    {
        var identity = state.Store.Identity;
        var signingKey = identity.SigningKey.ToArray();
        var image = QueueWholeFlowStorage.Bytes(state.Store);
        var position = state.Store.Position;
        for (var cycle = 0; cycle < P.ColdCount; cycle++)
        {
            state.Owner.Close();
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
            await NativePolicyEpochAssertions.UnchangedAsync(state, image, position);
            await NativePolicyEpochAssertions.ReadAsync(state, true, true, token);
            await NativePolicyEpochRefusals.CancelAsync(state);
            await NativePolicyEpochRefusals.ReplayAsync(state, token);
        }
    }
}

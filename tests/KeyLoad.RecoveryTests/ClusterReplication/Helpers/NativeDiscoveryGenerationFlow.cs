using KeyLoad.Clustering;

namespace KeyLoad.RecoveryTests;

internal static class NativeDiscoveryGenerationFlow
{
    internal static async Task RejectWithoutConsumingAsync(
        PeerDiscoveryFixture fixture, ReplicaNativeDiscoveryRequest original)
    {
        var before = fixture.Runtime.ReadActiveMembership();
        var caller = SiloAddress.FromParsableString(original.CallerSiloAddress);
        var changed = original with
        {
            CallerSiloAddress = SiloAddress.New(caller.Endpoint, checked(caller.Generation + 1)).ToParsableString()
        };
        var signed = PeerDiscoveryReplayTests.Resign(fixture, changed);
        fixture.Receiver.VerifyNativeDiscoveryRequest(signed);
        var refused = await fixture.ReceiveAsync(signed);
        await Assert.That(refused.Error).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(refused.Nonce).IsEqualTo(original.Nonce);
        await Assert.That(refused.Payload.IsEmpty).IsTrue();
        await Assert.That(refused.DiscoverySignature).IsEqualTo(string.Empty);
        await Assert.That(fixture.Runtime.ReadActiveMembership().SequenceEqual(before)).IsTrue();
    }
}

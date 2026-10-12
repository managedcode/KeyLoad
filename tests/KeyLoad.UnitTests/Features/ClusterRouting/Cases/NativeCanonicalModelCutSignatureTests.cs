using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeCanonicalModelCutSignatureTests
{
    private const int OverflowByte = 1;

    [Test]
    public async Task ActualNativeAuthenticatorBindsDomainNonceVoterAndCompletePayload()
    {
        using var fixture = new ReplicaSecurityFixture();
        var request = NativeCanonicalModelCutAdmissionFixture.Request();
        var payload = NativeSerialization.Serialize(request);
        var framed = NativeCanonicalModelCutSignatureFrame.Create(payload);
        byte[] expected = [.. "keyload.probe.native-canonical-model-cut.v1\0"u8, .. payload];
        await Assert.That(framed.SequenceEqual(expected)).IsTrue();
        var signature = fixture.Sender.SignDiscovery(framed, request.Nonce);
        fixture.Receiver.VerifyDiscovery(ReplicaSecurityFixture.VoterA, framed, request.Nonce, signature);
        var wrongNonce = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyDiscovery(
            ReplicaSecurityFixture.VoterA, framed, Guid.NewGuid(), signature));
        await Assert.That(wrongNonce.Code).IsEqualTo(ErrorCode.Unauthenticated);
        var changed = NativeCanonicalModelCutSignatureFrame.Create(NativeSerialization.Serialize(
            request with { ArmId = Guid.NewGuid() }));
        await RequireUnauthenticatedAsync(() => fixture.Receiver.VerifyDiscovery(
            ReplicaSecurityFixture.VoterA, changed, request.Nonce, signature));
        await RequireUnauthenticatedAsync(() => fixture.Receiver.VerifyDiscovery(
            ReplicaSecurityFixture.VoterB, framed, request.Nonce, signature));
        await RequireUnauthenticatedAsync(() => fixture.Receiver.VerifyDiscovery(
            ReplicaSecurityFixture.VoterA, payload, request.Nonce, signature));
        var discoverySignature = fixture.Sender.SignDiscovery(payload, request.Nonce);
        await RequireUnauthenticatedAsync(() => fixture.Receiver.VerifyDiscovery(
            ReplicaSecurityFixture.VoterA, framed, request.Nonce, discoverySignature));
    }

    [Test]
    public async Task SignatureFramePreservesMaximumOriginalPayloadAndRefusesEmptyOrOverflow()
    {
        using var fixture = new ReplicaSecurityFixture();
        var payload = new byte[NativeCanonicalModelCutCodec.MaximumRecordBytes];
        var nonce = Guid.NewGuid();
        var framed = NativeCanonicalModelCutSignatureFrame.Create(payload);
        var signature = fixture.Sender.SignDiscovery(framed, nonce);
        fixture.Receiver.VerifyDiscovery(ReplicaSecurityFixture.VoterA, framed, nonce, signature);
        // Arbitrary bytes exercise framing/MAC bounds only; they are not a valid witness or route result.
        foreach (var invalid in new[] { Array.Empty<byte>(), new byte[payload.Length + OverflowByte] })
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => NativeCanonicalModelCutSignatureFrame.Create(invalid));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        }
    }

    private static async Task RequireUnauthenticatedAsync(Action verify)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(verify);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Unauthenticated);
    }
}

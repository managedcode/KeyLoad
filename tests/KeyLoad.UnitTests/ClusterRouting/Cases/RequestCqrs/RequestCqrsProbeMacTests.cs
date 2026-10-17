using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsProbeMacTests
{
    [Test]
    public async Task RequestAndReplyUseTheFrozenDistinctDiagnosticHmacDomains()
    {
        using var fixture = new ReplicaSecurityFixture();
        using var mac = new PartitionMovementMac(fixture.Options.Secret.Span, PhysicalOwnerProbeProtocol.MaximumBodyBytes);
        var request = RequestCqrsProbeNativeWireValues.Request(fixture);
        var reply = RequestCqrsProbeNativeWireValues.Reply(request);
        var requestBytes = NativeSerialization.Serialize(request);
        var replyBytes = NativeSerialization.Serialize(reply);
        await Assert.That(RequestCqrsProbeNativeProtocol.RequestPurpose).IsEqualTo(RequestCqrsProbeNativeWireValues.RequestPurpose);
        await Assert.That(RequestCqrsProbeNativeProtocol.ReplyPurpose).IsEqualTo(RequestCqrsProbeNativeWireValues.ReplyPurpose);
        await Assert.That(mac.SignProbeRequest(request)).IsEqualTo(RequestCqrsProbeNativeWireValues.Digest(fixture.Options.Secret.Span,
            requestBytes, RequestCqrsProbeNativeWireValues.RequestPurpose));
        await Assert.That(mac.SignProbeReply(reply)).IsEqualTo(RequestCqrsProbeNativeWireValues.Digest(fixture.Options.Secret.Span,
            replyBytes, RequestCqrsProbeNativeWireValues.ReplyPurpose));
        var wrongRequest = request with
        {
            Signature = RequestCqrsProbeNativeWireValues.Digest(fixture.Options.Secret.Span,
            requestBytes, RequestCqrsProbeNativeWireValues.ReplyPurpose)
        };
        var wrongReply = reply with
        {
            Signature = RequestCqrsProbeNativeWireValues.Digest(fixture.Options.Secret.Span,
            replyBytes, RequestCqrsProbeNativeWireValues.RequestPurpose)
        };
        await Assert.That(mac.VerifyProbeRequest(wrongRequest)).IsFalse();
        await Assert.That(mac.VerifyProbeReply(wrongReply)).IsFalse();
        await Assert.That(mac.VerifyProbeRequest(request with
        {
            Signature = mac.Sign(requestBytes, reply: false)
        })).IsFalse();
        await Assert.That(mac.VerifyProbeReply(reply with
        {
            Signature = mac.Sign(replyBytes, reply: true)
        })).IsFalse();
    }

    [Test]
    public async Task UnsignedClaimOrAcknowledgementAndMutatedSignedRefusalCannotProveDisposal()
    {
        using var fixture = new ReplicaSecurityFixture();
        using var mac = new PartitionMovementMac(fixture.Options.Secret.Span, PhysicalOwnerProbeProtocol.MaximumBodyBytes);
        var request = RequestCqrsProbeNativeWireValues.Request(fixture);
        var unsigned = RequestCqrsProbeNativeWireValues.Reply(request);
        await Assert.That(mac.VerifyProbeReply(unsigned)).IsFalse();
        await Assert.That(mac.VerifyProbeReply(unsigned with
        {
            Acknowledged = true
        })).IsFalse();
        var refusal = unsigned with
        {
            Claim = null,
            Error = ErrorCode.UnsupportedCapability
        };
        await Assert.That(mac.VerifyProbeReply(refusal)).IsFalse();
        refusal = refusal with
        {
            Signature = mac.SignProbeReply(refusal)
        };
        await Assert.That(mac.VerifyProbeReply(refusal)).IsTrue();
        await Assert.That(refusal.Acknowledged).IsFalse();
        await Assert.That(mac.VerifyProbeReply(refusal with
        {
            Acknowledged = true
        })).IsFalse();
        await Assert.That(mac.VerifyProbeReply(refusal with
        {
            Error = null
        })).IsFalse();
        await Assert.That(mac.VerifyProbeReply(refusal with
        {
            Claim = request.Claim
        })).IsFalse();
        await Assert.That(mac.VerifyProbeReply(refusal with
        {
            Nonce = Guid.NewGuid()
        })).IsFalse();
        await Assert.That(mac.VerifyProbeReply(refusal with
        {
            Method = RequestCqrsProbeNativeMethod.Locate
        })).IsFalse();
        await Assert.That(mac.VerifyProbeReply(refusal with
        {
            Version = refusal.Version + 1
        })).IsFalse();
    }

    [Test]
    public void DisposedMacRejectsDiagnosticRequestAndReplySigningAndVerification()
    {
        using var fixture = new ReplicaSecurityFixture();
        var mac = new PartitionMovementMac(fixture.Options.Secret.Span, PhysicalOwnerProbeProtocol.MaximumBodyBytes);
        var request = RequestCqrsProbeNativeWireValues.Request(fixture);
        request = request with
        {
            Signature = mac.SignProbeRequest(request)
        };
        var reply = RequestCqrsProbeNativeWireValues.Reply(request);
        reply = reply with
        {
            Signature = mac.SignProbeReply(reply)
        };
        mac.Dispose();
        mac.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => mac.SignProbeRequest(request));
        Assert.ThrowsExactly<ObjectDisposedException>(() => mac.VerifyProbeRequest(request));
        Assert.ThrowsExactly<ObjectDisposedException>(() => mac.SignProbeReply(reply));
        Assert.ThrowsExactly<ObjectDisposedException>(() => mac.VerifyProbeReply(reply));
    }
}

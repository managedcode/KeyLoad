using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PhysicalOwnerProbeNativeMacTests
{
    [Test]
    public async Task OriginalCallAndOuterEnvelopeRetainIndependentNonceAndMacThroughGeneratedCodec()
    {
        using var fixture = new ReplicaSecurityFixture();
        using var inner = new PhysicalOwnerProbeMac(fixture.Options.Secret.Span);
        using var outer = new PartitionMovementMac(fixture.Options.Secret.Span, PhysicalOwnerProbeProtocol.MaximumBodyBytes);
        var call = PhysicalOwnerProbeNativeTestValues.Call(fixture);
        var request = PhysicalOwnerProbeNativeTestValues.Request(fixture, call, inner);
        request = request with
        {
            SenderSignature = outer.SignNativeRequest(request)
        };
        var restored = NativeSerialization.Deserialize<PartitionMovementGrainServiceRequest>(NativeSerialization.Serialize(request));
        var original = PhysicalOwnerProbeWire.DecodeCall(restored.Payload.Span);
        await Assert.That(PhysicalOwnerProbeWire.Encode(original).AsSpan().SequenceEqual(request.Payload.Span)).IsTrue();
        await Assert.That(original.RequestId).IsEqualTo(call.RequestId);
        await Assert.That(original.Nonce).IsEqualTo(call.Nonce);
        await Assert.That(PhysicalOwnerProbeNativeIdentity.ValidNonce(original.Nonce)).IsTrue();
        await Assert.That(Convert.FromBase64String(original.Nonce.Replace('-', '+').Replace('_', '/') + "==").Length).IsEqualTo(16);
        await Assert.That(restored.Nonce).IsEqualTo(request.Nonce);
        await Assert.That(restored.Payload.Span.SequenceEqual(request.Payload.Span)).IsTrue();
        await Assert.That(outer.VerifyNativeRequest(restored)).IsTrue();
        await Assert.That(inner.Verify(restored.Payload.Span, restored.Signature, reply: false)).IsTrue();
        await Assert.That(inner.Verify(restored.Payload.Span, restored.Signature, reply: true)).IsFalse();
        await Assert.That(inner.Verify(restored.Payload.Span, restored.SenderSignature, reply: false)).IsFalse();
        await Assert.That(outer.VerifyNativeRequest(restored with
        {
            SenderSignature = restored.Signature
        })).IsFalse();
    }

    [Test]
    public async Task OriginalOuterProofRejectsPurposeQueryIdentityGenerationNonceExpiryAndInnerSignatureMutation()
    {
        using var fixture = new ReplicaSecurityFixture();
        using var inner = new PhysicalOwnerProbeMac(fixture.Options.Secret.Span);
        using var outer = new PartitionMovementMac(fixture.Options.Secret.Span, PhysicalOwnerProbeProtocol.MaximumBodyBytes);
        var request = PhysicalOwnerProbeNativeTestValues.Request(fixture, PhysicalOwnerProbeNativeTestValues.Call(fixture), inner);
        request = request with
        {
            SenderSignature = outer.SignNativeRequest(request)
        };
        var changed = new[]
        {
            request with { Channel = PartitionMovementGrainServiceChannel.Outcome },
            request with { Query = false },
            request with { CallerVoter = ReplicaSecurityFixture.UnknownVoter },
            request with { CallerSiloAddress = fixture.OtherGeneration },
            request with { DestinationVoter = ReplicaSecurityFixture.UnknownVoter },
            request with { DestinationSiloAddress = fixture.OtherGeneration },
            request with { Nonce = Guid.NewGuid() },
            request with { ExpiresAt = request.ExpiresAt.AddTicks(1) },
            request with { Signature = new string('0', 64) }
        };
        foreach (var mutation in changed)
        {
            await Assert.That(outer.VerifyNativeRequest(mutation)).IsFalse();
        }
        var altered = changed[^1] with
        {
            SenderSignature = outer.SignNativeRequest(changed[^1])
        };
        await Assert.That(outer.VerifyNativeRequest(altered)).IsTrue();
        await Assert.That(inner.Verify(altered.Payload.Span, altered.Signature, reply: false)).IsFalse();
    }

    [Test]
    public async Task SafeUnsignedRefusalIsNotNativeProofAndSignedRefusalBindsEveryField()
    {
        using var fixture = new ReplicaSecurityFixture();
        using var outer = new PartitionMovementMac(fixture.Options.Secret.Span, PhysicalOwnerProbeProtocol.MaximumBodyBytes);
        var refusal = new PartitionMovementGrainServiceReply(ReadOnlyMemory<byte>.Empty, string.Empty,
            ErrorCode.UnsupportedCapability, PhysicalOwnerProbeProtocol.Unavailable, Guid.NewGuid(), string.Empty);
        var restored = NativeSerialization.Deserialize<PartitionMovementGrainServiceReply>(NativeSerialization.Serialize(refusal));
        await Assert.That(restored.Payload.IsEmpty).IsTrue();
        await Assert.That(restored.Signature).IsEqualTo(string.Empty);
        await Assert.That(outer.VerifyNativeReply(restored)).IsFalse();
        var signed = restored with
        {
            SenderSignature = outer.SignNativeReply(restored)
        };
        await Assert.That(outer.VerifyNativeReply(signed)).IsTrue();
        await Assert.That(outer.VerifyNativeReply(signed with
        {
            Nonce = Guid.NewGuid()
        })).IsFalse();
        await Assert.That(outer.VerifyNativeReply(signed with
        {
            Error = ErrorCode.OwnershipLost
        })).IsFalse();
        await Assert.That(outer.VerifyNativeReply(signed with
        {
            SafeDetail = string.Empty
        })).IsFalse();
        await Assert.That(outer.VerifyNativeReply(signed with
        {
            Signature = new string('0', 64)
        })).IsFalse();
    }

    [Test]
    public void DisposedOriginalAndOuterMacsCannotSignOrVerifyValidShapedProofs()
    {
        using var fixture = new ReplicaSecurityFixture();
        var inner = new PhysicalOwnerProbeMac(fixture.Options.Secret.Span);
        var outer = new PartitionMovementMac(fixture.Options.Secret.Span, PhysicalOwnerProbeProtocol.MaximumBodyBytes);
        var request = PhysicalOwnerProbeNativeTestValues.Request(fixture, PhysicalOwnerProbeNativeTestValues.Call(fixture), inner);
        request = request with
        {
            SenderSignature = outer.SignNativeRequest(request)
        };
        inner.Dispose();
        outer.Dispose();
        inner.Dispose();
        outer.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => inner.Sign(request.Payload.Span, reply: false));
        Assert.ThrowsExactly<ObjectDisposedException>(() => inner.Verify(request.Payload.Span, request.Signature, reply: false));
        Assert.ThrowsExactly<ObjectDisposedException>(() => outer.SignNativeRequest(request));
        Assert.ThrowsExactly<ObjectDisposedException>(() => outer.VerifyNativeRequest(request));
    }
}

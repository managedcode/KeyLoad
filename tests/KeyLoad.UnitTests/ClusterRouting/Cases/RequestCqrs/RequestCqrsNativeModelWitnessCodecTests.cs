using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsNativeModelWitnessCodecTests
{
    [Test]
    public async Task ExactSignedGeneratedPacketsSurvivePrivateWitnessCodec()
    {
        using var fixture = new RequestCqrsNativeModelWitnessCase();
        var original = fixture.Create();
        var bytes = RequestCqrsNativeModelInventory.Write(original);
        var restored = RequestCqrsNativeModelInventory.Read(bytes);
        await Assert.That(restored.NativeRequest.SequenceEqual(original.NativeRequest)).IsTrue();
        await Assert.That(restored.NativeReply!.SequenceEqual(original.NativeReply!)).IsTrue();
        var request = RequestCqrsNativeModelInventory.Request(restored);
        var reply = RequestCqrsNativeModelInventory.Reply(restored);
        await Assert.That(fixture.Mac.VerifyNativeRequest(request)).IsTrue();
        await Assert.That(fixture.Mac.VerifyNativeReply(reply)).IsTrue();
        await Assert.That(fixture.Mac.Verify(request.Payload.Span, request.Signature, false)).IsTrue();
        await Assert.That(fixture.Mac.Verify(reply.Payload.Span, reply.Signature, true)).IsTrue();
        await Assert.That(fixture.Mac.VerifyNativeRequest(request with
        {
            Nonce = Guid.NewGuid()
        })).IsFalse();
        await Assert.That(fixture.Mac.VerifyNativeReply(reply with
        {
            Nonce = Guid.NewGuid()
        })).IsFalse();
        RequestCqrsNativeModelInventory.RequireMarkers([restored], [RequestCqrsNativeModelWitnessCase.Marker(original)]);
    }

    [Test]
    public async Task BeforeDispatchHasOnlyActualRequestAndAfterRequiresActualCorrelatedReply()
    {
        using var fixture = new RequestCqrsNativeModelWitnessCase();
        var before = fixture.Create(false);
        var after = fixture.Create();
        var restored = RequestCqrsNativeModelInventory.Read(RequestCqrsNativeModelInventory.Write(before));
        await Assert.That(restored.NativeReply is null).IsTrue();
        Assert.ThrowsExactly<InvalidOperationException>(() => RequestCqrsNativeModelInventory.Reply(restored));
        Assert.ThrowsExactly<InvalidOperationException>(() => RequestCqrsNativeModelInventory.Write(before with { NativeReply = after.NativeReply }));
        Assert.ThrowsExactly<InvalidOperationException>(() => RequestCqrsNativeModelInventory.Write(after with { NativeReply = null }));
        Assert.ThrowsExactly<InvalidOperationException>(() => RequestCqrsNativeModelInventory.Write(after with { NativeReply = [] }));
        var changedReply = RequestCqrsNativeModelInventory.Reply(after) with
        {
            Nonce = Guid.NewGuid()
        };
        Assert.ThrowsExactly<InvalidOperationException>(() => RequestCqrsNativeModelInventory.Write(after with { NativeReply = NativeSerialization.Serialize(changedReply) }));
    }

    [Test]
    public async Task EncodingUnsignedRefusalDoesNotTurnItIntoNativeAcknowledgement()
    {
        using var fixture = new RequestCqrsNativeModelWitnessCase();
        var original = fixture.Create();
        var request = RequestCqrsNativeModelInventory.Request(original);
        var refusal = new KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeReply(ReadOnlyMemory<byte>.Empty,
            string.Empty, ErrorCode.UnsupportedCapability, null, request.Nonce, string.Empty);
        var retained = RequestCqrsNativeModelInventory.Read(RequestCqrsNativeModelInventory.Write(
            original with
            {
                NativeReply = NativeSerialization.Serialize(refusal)
            }));
        var actual = RequestCqrsNativeModelInventory.Reply(retained);
        await Assert.That(fixture.Mac.VerifyNativeReply(actual)).IsFalse();
        await Assert.That(actual.Payload.IsEmpty).IsTrue();
        await Assert.That(actual.Error).IsEqualTo(ErrorCode.UnsupportedCapability);
    }

    [Test]
    public void StructuralIdentityAndActualCallerTupleAreRequired()
    {
        using var fixture = new RequestCqrsNativeModelWitnessCase();
        var original = fixture.Create();
        var invalid = new[]
        {
            original with { Version = original.Version + 1 }, original with { SessionId = string.Empty },
            original with { ArmId = Guid.Empty }, original with { RequestId = Guid.Empty },
            original with { CommandId = Guid.Empty }, original with { ConnectionId = Guid.Empty },
            original with { Voter = original.Voter + "-changed" },
            original with { SiloAddress = original.SiloAddress + "-changed" },
            original with { Phase = RequestCqrsProbePhase.RequestStarted }, original with { NativeRequest = [] }
        };
        foreach (var value in invalid)
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => RequestCqrsNativeModelInventory.Write(value));
        }
        _ = RequestCqrsNativeModelInventory.Read(RequestCqrsNativeModelInventory.Write(original));
    }
}

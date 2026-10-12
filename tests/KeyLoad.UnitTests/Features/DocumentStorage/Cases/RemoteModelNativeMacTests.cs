using System.Security.Cryptography;
using KeyLoad.Server.Features.DocumentStorage;
namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class RemoteModelNativeMacTests
{
    [Test]
    public async Task GeneratedOriginalRequestAndReplyRoundTripWithAllSignedNativeFields()
    {
        await RemoteModelNativeMacFixture.RunAsync(async fixture =>
        {
            var request = fixture.Request();
            request = request with { SenderSignature = fixture.Mac.SignNativeRequest(request) };
            var bytes = NativeSerialization.Serialize(request);
            var restored = NativeSerialization.Deserialize<RemoteModelNativeRequest>(bytes);
            await Assert.That(NativeSerialization.Serialize(restored).SequenceEqual(bytes)).IsTrue();
            await Assert.That(restored.Payload.Span.SequenceEqual(request.Payload.Span)).IsTrue();
            await Assert.That(restored.Signature).IsEqualTo(request.Signature);
            await Assert.That(restored.SourceOwner.VoterIds.SequenceEqual(request.SourceOwner.VoterIds)).IsTrue();
            await Assert.That(restored.DestinationOwner.VoterIds.SequenceEqual(request.DestinationOwner.VoterIds)).IsTrue();
            await Assert.That(restored.CallerSiloAddress).IsEqualTo(request.CallerSiloAddress);
            await Assert.That(restored.DestinationSiloAddress).IsEqualTo(request.DestinationSiloAddress);
            await Assert.That(restored.ExpiresAt).IsEqualTo(request.ExpiresAt);
            await Assert.That(fixture.Mac.VerifyNativeRequest(restored)).IsTrue();
            var reply = fixture.Reply(request);
            reply = reply with { SenderSignature = fixture.Mac.SignNativeReply(reply) };
            var replyBytes = NativeSerialization.Serialize(reply);
            var retained = NativeSerialization.Deserialize<RemoteModelNativeReply>(replyBytes);
            await Assert.That(NativeSerialization.Serialize(retained).SequenceEqual(replyBytes)).IsTrue();
            await Assert.That(retained.Nonce).IsEqualTo(request.Nonce);
            await Assert.That(fixture.Mac.VerifyNativeReply(retained)).IsTrue();
        });
    }

    [Test]
    public async Task OriginalNativeProofRefusesEveryRequestFieldSubstitution()
    {
        await RemoteModelNativeMacFixture.RunAsync(async fixture =>
        {
            var request = fixture.Request();
            request = request with { SenderSignature = fixture.Mac.SignNativeRequest(request) };
            var changed = new[]
            {
                request with { Channel = RemoteModelNativeChannel.EventVector },
                request with { Payload = new byte[] { 0, 1, 127, 128, 254 } },
                request with { Signature = new string('0', request.Signature.Length) },
                request with { SourceOwner = request.SourceOwner with { PhysicalShardId = Guid.NewGuid() } },
                request with { SourceOwner = request.SourceOwner with { Incarnation = Guid.NewGuid() } },
                request with { SourceOwner = request.SourceOwner with { VoterIds = ["control-b", "control-a", "control-c"] } },
                request with { SourceOwner = request.SourceOwner with { PlacementEpoch = request.SourceOwner.PlacementEpoch + 1 } },
                request with { DestinationOwner = request.DestinationOwner with { PhysicalShardId = Guid.NewGuid() } },
                request with { DestinationOwner = request.DestinationOwner with { Incarnation = Guid.NewGuid() } },
                request with { DestinationOwner = request.DestinationOwner with { VoterIds = ["destination-b", "destination-a", "destination-c"] } },
                request with { DestinationOwner = request.DestinationOwner with { PlacementEpoch = request.DestinationOwner.PlacementEpoch + 1 } },
                request with { CallerVoter = "control-b" },
                request with { CallerSiloAddress = RemoteModelNativeMacFixture.Address(11111, 32) },
                request with { DestinationVoter = "destination-c" },
                request with { DestinationSiloAddress = RemoteModelNativeMacFixture.Address(11112, 48) },
                request with { Nonce = Guid.NewGuid() },
                request with { ExpiresAt = request.ExpiresAt.AddMilliseconds(1) },
                request with { SenderSignature = new string('0', request.SenderSignature.Length) }
            };
            foreach (var candidate in changed)
            { await Assert.That(fixture.Mac.VerifyNativeRequest(candidate)).IsFalse(); }
            await Assert.That(fixture.Mac.VerifyNativeRequest(request)).IsTrue();
        });
    }

    [Test]
    public async Task OriginalNativeReplyProofRefusesEveryReplyFieldAndForeignCredential()
    {
        await RemoteModelNativeMacFixture.RunAsync(async fixture =>
        {
            var request = fixture.Request();
            var reply = fixture.Reply(request);
            reply = reply with { SenderSignature = fixture.Mac.SignNativeReply(reply) };
            var changed = new[]
            {
                reply with { Payload = new byte[] { 254, 128, 127, 1, 0 } },
                reply with { Signature = new string('0', reply.Signature.Length) },
                reply with { Error = ErrorCode.OwnershipLost },
                reply with { SafeDetail = "The configured owner is unavailable." },
                reply with { Nonce = Guid.NewGuid() },
                reply with { SenderSignature = new string('z', reply.SenderSignature.Length) }
            };
            foreach (var candidate in changed)
            { await Assert.That(fixture.Mac.VerifyNativeReply(candidate)).IsFalse(); }
            using var other = new RemoteModelNativeMacFixture();
            await Assert.That(other.Mac.VerifyNativeReply(reply)).IsFalse();
            await Assert.That(fixture.Mac.VerifyNativeReply(reply)).IsTrue();
        });
    }

    [Test]
    public async Task NativePurposesCannotReuseOriginalInnerRequestOrReplyMac()
    {
        await RemoteModelNativeMacFixture.RunAsync(async fixture =>
        {
            var request = fixture.Request();
            var frame = new RemoteModelNativeMacFrame(RemoteModelNativeProtocol.NativeRequestPurpose,
                SHA256.HashData(NativeSerialization.Serialize(request)));
            var bytes = NativeSerialization.Serialize(frame);
            var native = fixture.Mac.SignNativeRequest(request);
            await Assert.That(native == fixture.Mac.Sign(bytes, false)).IsFalse();
            await Assert.That(native == fixture.Mac.Sign(bytes, true)).IsFalse();
            await Assert.That(fixture.Mac.VerifyNativeRequest(request with { SenderSignature = fixture.Mac.Sign(bytes, false) })).IsFalse();
            var reply = fixture.Reply(request);
            await Assert.That(fixture.Mac.VerifyNativeReply(reply with { SenderSignature = native })).IsFalse();
            var replyProof = fixture.Mac.SignNativeReply(reply);
            await Assert.That(fixture.Mac.VerifyNativeRequest(request with { SenderSignature = replyProof })).IsFalse();
        });
    }
}

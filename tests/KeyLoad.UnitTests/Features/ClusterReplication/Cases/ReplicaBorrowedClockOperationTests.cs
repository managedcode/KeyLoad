using KeyLoad.Clustering;
using KeyLoad.Replication;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests;

internal sealed class ReplicaBorrowedClockOperationTests
{
    private const int CompleteScanLimit = 4096;
    private static readonly DateTimeOffset Epoch = new(2041, 2, 3, 4, 5, 6, TimeSpan.Zero);

    [Test]
    public async Task OriginalSignedExpiryRefusesWithoutEffectsThenBorrowedClockHealthyReplyAndReplayStayExact()
    {
        var clock = new ControlledReadClock(Epoch);
        using var fixture = new ReplicaSecurityFixture(clock: clock);
        var original = fixture.Read();
        var state = State(fixture);
        var position = fixture.Database.Store.Position;
        await Assert.That(original.Timestamp).IsEqualTo(Epoch.ToUnixTimeMilliseconds());
        var lifetime = UnitRoutingOptions.Transport().Value.EnvelopeLifetime;
        clock.Advance(lifetime + TimeSpan.FromMilliseconds(ReplicaSecurityFixture.TimeMarginMilliseconds));
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(original));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(State(fixture).SequenceEqual(state)).IsTrue();
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(position);
        var fresh = fixture.Read();
        var accepted = fixture.Receiver.VerifyRequest(fresh);
        await RequestAsync(fixture, fresh, accepted, (Epoch + lifetime +
            TimeSpan.FromMilliseconds(ReplicaSecurityFixture.TimeMarginMilliseconds)).ToUnixTimeMilliseconds());
        var reply = fixture.Receiver.CreateReply(accepted, ReplicaSecurityFixture.EmptyRead);
        fixture.Sender.VerifyReply(fresh, reply);
        await ReplyAsync(fixture, fresh, reply, clock.GetUtcNow().ToUnixTimeMilliseconds());
        var replay = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(fresh));
        await Assert.That(replay.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(State(fixture).SequenceEqual(state)).IsTrue();
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(position);
    }

    private static async Task RequestAsync(ReplicaSecurityFixture fixture, ReplicaPeerEnvelope original,
        ReplicaPeerEnvelope accepted, long timestamp)
    {
        await Assert.That(accepted.Version).IsEqualTo(ReplicaTransportProtocol.Version);
        await Assert.That(accepted.Incarnation).IsEqualTo(fixture.Configuration.Incarnation);
        await Assert.That(accepted.Sender).IsEqualTo(ReplicaSecurityFixture.VoterA);
        await Assert.That(accepted.Recipient).IsEqualTo(ReplicaSecurityFixture.VoterB);
        await Assert.That(accepted.Method).IsEqualTo(ReplicaRpc.ReadBarrier);
        await Assert.That(accepted.RuntimeAddress).IsEqualTo(fixture.Discovery.RuntimeAddress);
        await Assert.That(accepted.Timestamp).IsEqualTo(timestamp);
        await Assert.That(accepted.Nonce).IsEqualTo(original.Nonce);
        await Assert.That(accepted.Nonce).IsNotEqualTo(Guid.Empty);
        await Assert.That(accepted.Payload.Span.SequenceEqual(ReplicaSecurityFixture.EmptyRead)).IsTrue();
        await Assert.That(accepted.Signature.Span.SequenceEqual(original.Signature.Span)).IsTrue();
    }

    private static async Task ReplyAsync(ReplicaSecurityFixture fixture, ReplicaPeerEnvelope request,
        ReplicaPeerReply reply, long timestamp)
    {
        await Assert.That(reply.Version).IsEqualTo(ReplicaTransportProtocol.Version);
        await Assert.That(reply.Incarnation).IsEqualTo(fixture.Configuration.Incarnation);
        await Assert.That(reply.Sender).IsEqualTo(ReplicaSecurityFixture.VoterB);
        await Assert.That(reply.Recipient).IsEqualTo(ReplicaSecurityFixture.VoterA);
        await Assert.That(reply.Method).IsEqualTo(ReplicaRpc.ReadBarrier);
        await Assert.That(reply.RuntimeAddress).IsEqualTo(fixture.Discovery.RuntimeAddress);
        await Assert.That(reply.Timestamp).IsEqualTo(timestamp);
        await Assert.That(reply.RequestNonce).IsEqualTo(request.Nonce);
        await Assert.That(reply.Payload.Span.SequenceEqual(ReplicaSecurityFixture.EmptyRead)).IsTrue();
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        await Assert.That(reply.Signature.Length).IsEqualTo(request.Signature.Length);
    }

    private static string[] State(ReplicaSecurityFixture fixture) => fixture.Database.Store.Read(view =>
    {
        var page = view.Scan([], CompleteScanLimit);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The complete peer fixture exceeds its bounded oracle.");
        }
        return page.Records.Select(record => Convert.ToHexString(record.Key.Span) + ":" +
            Convert.ToHexString(record.Value.Span)).ToArray();
    });
}

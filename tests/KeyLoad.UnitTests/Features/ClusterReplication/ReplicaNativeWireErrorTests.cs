using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests;

/// <summary>AC-REP-006: native wire errors cannot consume authenticated replay capacity.</summary>
internal sealed class ReplicaNativeWireErrorTests
{
    private const byte FenceMutationBit = 1;

    /// <summary>Authenticated unsupported or trailing payloads reject before nonce admission.</summary>
    [Test]
    [Arguments(ReplicaRpc.RequestVote, true)]
    [Arguments(ReplicaRpc.RequestVote, false)]
    [Arguments(ReplicaRpc.Forward, true)]
    [Arguments(ReplicaRpc.Forward, false)]
    public async Task InvalidNativeWireDoesNotConsumeItsNonce(ReplicaRpc method, bool unsupported)
    {
        using var fixture = new ReplicaSecurityFixture();
        var valid = method == ReplicaRpc.RequestVote ? fixture.Vote() : fixture.Forward(OperationKind.Membership);
        var invalid = fixture.Resign(valid with { Payload = InvalidBytes(valid.Payload, unsupported) });
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(invalid));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(failure.Message).IsEqualTo(ReplicaTransportProtocol.InvalidPayload);
        var owned = fixture.Receiver.VerifyRequest(valid);
        await Assert.That(owned.Nonce).IsEqualTo(valid.Nonce);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(owned)).Code)
            .IsEqualTo(ErrorCode.Unauthenticated);
    }

    /// <summary>The direct native reader retains explicit format and corruption diagnostics.</summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task DirectNativeReaderKeepsItsOriginalErrorClassification(bool unsupported)
    {
        using var fixture = new ReplicaSecurityFixture();
        var invalid = InvalidBytes(fixture.Vote().Payload, unsupported);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => ReplicaProtocolCodec.Deserialize<VoteRequest>(invalid));
        await Assert.That(failure.Code).IsEqualTo(unsupported ? ErrorCode.FormatUnsupported : ErrorCode.Corruption);
    }

    /// <summary>Wire translation cannot override the earlier genuine MAC or recipient checks.</summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AuthenticationPrecedesMalformedNativeAdmission(bool wrongMac)
    {
        using var fixture = new ReplicaSecurityFixture();
        var valid = fixture.Vote();
        var invalid = valid with { Payload = InvalidBytes(valid.Payload, unsupported: true) };
        if (!wrongMac)
        {
            invalid = fixture.Resign(invalid with { Recipient = ReplicaSecurityFixture.VoterC });
        }
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(invalid)).Code)
            .IsEqualTo(ErrorCode.Unauthenticated);
        var owned = fixture.Receiver.VerifyRequest(valid);
        await Assert.That(owned.Nonce).IsEqualTo(valid.Nonce);
    }

    private static byte[] InvalidBytes(ReadOnlyMemory<byte> payload, bool unsupported)
    {
        var bytes = new byte[checked(payload.Length + (unsupported ? 0 : 1))];
        payload.Span.CopyTo(bytes);
        if (unsupported)
        {
            bytes[0] ^= FenceMutationBit;
        }
        return bytes;
    }
}

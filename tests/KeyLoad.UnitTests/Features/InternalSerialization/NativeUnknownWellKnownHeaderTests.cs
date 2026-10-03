using System.Text;
using KeyLoad.Features.InternalSerialization;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeUnknownWellKnownHeaderTests
{
    private const string NullJson = "null";
    private const string Voter = "voter";
    [Test]
    [Arguments(NativeUnknownWellKnownHeaderScope.Envelope, 0u)]
    [Arguments(NativeUnknownWellKnownHeaderScope.Value, 1u)]
    [Arguments(NativeUnknownWellKnownHeaderScope.Member, 1u)]
    [Arguments(NativeUnknownWellKnownHeaderScope.UnknownMember, 6u)]
    [Arguments(NativeUnknownWellKnownHeaderScope.UnknownMember, 7u)]
    [Arguments(NativeUnknownWellKnownHeaderScope.UnknownMember, 1_000u)]
    public async Task AcR17002UnknownMetadataIsCorruptionAndTheSameWriterTwinRemainsReadable(
        NativeUnknownWellKnownHeaderScope scope, uint delta)
    {
        var valid = NativeUnknownWellKnownHeaderFixture.Encode(scope, delta, malformed: false);
        var invalid = NativeUnknownWellKnownHeaderFixture.Encode(scope, delta, malformed: true);
        var expected = new NativeUnknownWellKnownHeaderRecord(NativeUnknownWellKnownHeaderFixture.Canary,
            NativeUnknownWellKnownHeaderFixture.Number);
        NativeSerialization.Validate(valid);
        await Assert.That(NativeSerialization.Deserialize<NativeUnknownWellKnownHeaderRecord>(valid)).IsEqualTo(expected);
        await Assert.That(valid.AsSpan().SequenceEqual(invalid)).IsFalse();
        var syntax = Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Validate(invalid));
        var typed = Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Deserialize<NativeUnknownWellKnownHeaderRecord>(invalid));
        await Assert.That(syntax.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(typed.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(typed.Message).IsEqualTo(NativePayloadVersion.InvalidPayload);
        await Assert.That(typed.Message).DoesNotContain(NativeUnknownWellKnownHeaderFixture.Canary);
        await Assert.That(NativeSerialization.Deserialize<NativeUnknownWellKnownHeaderRecord>(valid)).IsEqualTo(expected);
    }

    [Test]
    public async Task AcR17002OriginalCurrentReplicaNullMetadataReproducerIsCorruption()
    {
        var invalid = Encoding.UTF8.GetBytes(NullJson);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Validate(invalid)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Deserialize<object>(invalid)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        var expected = new VoteRequest(Voter, 2, 0, 0);
        var valid = ReplicaProtocolCodec.Serialize(expected);
        await Assert.That(ReplicaProtocolCodec.Deserialize<VoteRequest>(valid)).IsEqualTo(expected);
        invalid.CopyTo(valid, ReplicaProtocol.PayloadPrefixBytes);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => ReplicaProtocolCodec.Deserialize<VoteRequest>(valid)).Code)
            .IsEqualTo(ErrorCode.Corruption);
    }
}

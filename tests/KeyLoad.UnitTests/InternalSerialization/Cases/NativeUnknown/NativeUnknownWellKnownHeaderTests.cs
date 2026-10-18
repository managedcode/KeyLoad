namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeUnknownWellKnownHeaderTests
{
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

}

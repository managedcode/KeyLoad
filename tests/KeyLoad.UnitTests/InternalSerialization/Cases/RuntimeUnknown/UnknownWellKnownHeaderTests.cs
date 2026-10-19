namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class UnknownWellKnownHeaderTests
{
    [Test]
    [Arguments(UnknownWellKnownHeaderScope.Envelope, 0u)]
    [Arguments(UnknownWellKnownHeaderScope.Value, 1u)]
    [Arguments(UnknownWellKnownHeaderScope.Member, 1u)]
    [Arguments(UnknownWellKnownHeaderScope.UnknownMember, 6u)]
    [Arguments(UnknownWellKnownHeaderScope.UnknownMember, 7u)]
    [Arguments(UnknownWellKnownHeaderScope.UnknownMember, 1_000u)]
    public async Task AcR17002UnknownMetadataIsCorruptionAndTheSameWriterTwinRemainsReadable(
        UnknownWellKnownHeaderScope scope, uint delta)
    {
        var valid = UnknownWellKnownHeaderFixture.Encode(scope, delta, malformed: false);
        var invalid = UnknownWellKnownHeaderFixture.Encode(scope, delta, malformed: true);
        var expected = new UnknownWellKnownHeaderRecord(UnknownWellKnownHeaderFixture.Canary,
            UnknownWellKnownHeaderFixture.Number);
        OrleansSerialization.Validate(valid);
        await Assert.That(OrleansSerialization.Deserialize<UnknownWellKnownHeaderRecord>(valid)).IsEqualTo(expected);
        await Assert.That(valid.AsSpan().SequenceEqual(invalid)).IsFalse();
        var syntax = Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Validate(invalid));
        var typed = Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Deserialize<UnknownWellKnownHeaderRecord>(invalid));
        await Assert.That(syntax.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(typed.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(typed.Message).IsEqualTo(PayloadVersion.InvalidPayload);
        await Assert.That(typed.Message).DoesNotContain(UnknownWellKnownHeaderFixture.Canary);
        await Assert.That(OrleansSerialization.Deserialize<UnknownWellKnownHeaderRecord>(valid)).IsEqualTo(expected);
    }

}

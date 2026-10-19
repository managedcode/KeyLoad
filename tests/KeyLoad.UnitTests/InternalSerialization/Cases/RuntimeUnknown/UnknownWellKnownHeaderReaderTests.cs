namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class UnknownWellKnownHeaderReaderTests
{
    [Test]
    [Arguments(0u)]
    [Arguments(6u)]
    [Arguments(7u)]
    [Arguments(1_000u)]
    public async Task AcR17002UnknownHeaderProbeDoesNotAdvanceTheOriginalCursorOrSession(uint delta)
    {
        var bytes = UnknownWellKnownHeaderReaderFixture.Header(UnknownWellKnownHeaderShape.Unknown, delta);
        var direct = UnknownWellKnownHeaderReaderFixture.Observe(bytes, guarded: false, stream: false);
        var guarded = UnknownWellKnownHeaderReaderFixture.Observe(bytes, guarded: true, stream: false);
        _ = UnknownWellKnownHeaderReaderFixture.RequireOriginalFailure(direct);
        await Assert.That(guarded.Error).IsTypeOf<KeyLoadException>();
        await Assert.That(((KeyLoadException)guarded.Error!).Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(guarded.Header).IsNull();
        await SameState(direct, guarded);
        await Assert.That(guarded.Remaining).IsEqualTo(1);
        await Assert.That(guarded.Position).IsEqualTo(bytes.Length - 1L);
        await Assert.That(guarded.ObjectReference).IsEqualTo(1u);
        await Assert.That(guarded.FirstTypeReference).IsEqualTo(typeof(UnknownWellKnownHeaderRecord));
        await Assert.That(guarded.SecondTypeReference).IsFalse();
    }

    [Test]
    [Arguments(UnknownWellKnownHeaderShape.Known)]
    [Arguments(UnknownWellKnownHeaderShape.Null)]
    [Arguments(UnknownWellKnownHeaderShape.Expected)]
    [Arguments(UnknownWellKnownHeaderShape.Encoded)]
    public async Task AcR17002KnownNullExpectedAndEncodedHeadersRetainTheOfficialResult(UnknownWellKnownHeaderShape shape)
    {
        var bytes = UnknownWellKnownHeaderReaderFixture.Header(shape, 7);
        var direct = UnknownWellKnownHeaderReaderFixture.Observe(bytes, guarded: false, stream: false);
        var guarded = UnknownWellKnownHeaderReaderFixture.Observe(bytes, guarded: true, stream: false);
        await Assert.That(direct.Error).IsNull();
        await Assert.That(guarded.Error).IsNull();
        await Assert.That(guarded.Header!.Value.FieldIdDelta).IsEqualTo(direct.Header!.Value.FieldIdDelta);
        await Assert.That(guarded.Header.Value.FieldType).IsEqualTo(direct.Header.Value.FieldType);
        await Assert.That(guarded.Header.Value.WireType).IsEqualTo(direct.Header.Value.WireType);
        await Assert.That(guarded.Header.Value.SchemaType).IsEqualTo(direct.Header.Value.SchemaType);
        await SameState(direct, guarded);
        await Assert.That(guarded.Remaining).IsEqualTo(1);
    }

    [Test]
    [Arguments(0u)]
    [Arguments(7u)]
    public async Task AcR17002NonSpanLookupFailuresRemainOriginalAndDoNotReplaySharedIo(uint delta)
    {
        var bytes = UnknownWellKnownHeaderReaderFixture.Header(UnknownWellKnownHeaderShape.Unknown, delta);
        var direct = UnknownWellKnownHeaderReaderFixture.Observe(bytes, guarded: false, stream: true);
        var guarded = UnknownWellKnownHeaderReaderFixture.Observe(bytes, guarded: true, stream: true);
        var expected = UnknownWellKnownHeaderReaderFixture.RequireOriginalFailure(direct);
        var actual = UnknownWellKnownHeaderReaderFixture.RequireOriginalFailure(guarded);
        await Assert.That(actual.Message).IsEqualTo(expected.Message);
        await SameState(direct, guarded);
        await Assert.That(guarded.Remaining).IsEqualTo(1);
    }

    private static async Task SameState(UnknownWellKnownHeaderObservation expected, UnknownWellKnownHeaderObservation actual)
    {
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await Assert.That(actual.Remaining).IsEqualTo(expected.Remaining);
        await Assert.That(actual.ObjectReference).IsEqualTo(expected.ObjectReference);
        await Assert.That(actual.FirstTypeReference).IsEqualTo(expected.FirstTypeReference);
        await Assert.That(actual.SecondTypeReference).IsEqualTo(expected.SecondTypeReference);
    }
}

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class InspectionTests
{
    [Test]
    public async Task AcIs002InspectionUsesTheAuthoritativeEnvelope()
    {
        var expected = new OutboxHead(1, 1, 1, 1);
        var actual = OrleansSerialization.Deserialize<OutboxHead>(OrleansSerialization.Serialize(expected));
        await Assert.That(actual).IsEqualTo(expected);
        using var session = OrleansSerialization.Serializer.SessionPool.GetSession();
        await Assert.That(OrleansSerialization.Measure(expected, session)).IsEqualTo((long)OrleansSerialization.Serialize(expected).Length);
    }

    [Test]
    public async Task AcIs002InspectionRejectsWrongDynamicRootBeforeItsCodec()
    {
        var bytes = OrleansSerialization.Serialize(new QueueCounters(1, 1, 1, 1, 1));
        KeyLoadException? failure = null;
        try
        {
            _ = OrleansSerialization.Deserialize<OutboxHead>(bytes);
        }
        catch (KeyLoadException exception)
        {
            failure = exception;
        }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    [Arguments(EnvelopeFault.WrongRoot)]
    [Arguments(EnvelopeFault.MissingVersion)]
    [Arguments(EnvelopeFault.MissingValue)]
    public async Task AcIs002InspectionRejectsMalformedEnvelopeBeforeAdmission(EnvelopeFault fault)
    {
        using var fixture = new SerializerFixture();
        KeyLoadException? failure = null;
        try
        {
            _ = OrleansSerialization.Deserialize<OutboxHead>(fixture.EncodeEnvelope(fault));
        }
        catch (KeyLoadException exception)
        {
            failure = exception;
        }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Code).IsEqualTo(fault == EnvelopeFault.MissingVersion
            ? ErrorCode.FormatUnsupported : ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(fault == EnvelopeFault.MissingVersion
            ? PayloadVersion.UnsupportedVersion : PayloadVersion.InvalidPayload);
    }
}

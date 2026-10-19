namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class InspectionTests
{
    private static readonly SerializerContext Context = SerializerProviders.CreateInspection(typeof(OutboxHead));

    [Test]
    public async Task AcIs002InspectionUsesTheAuthoritativeEnvelope()
    {
        var expected = new OutboxHead(1, 1, 1, 1);
        var actual = OrleansSerialization.Deserialize<OutboxHead>(OrleansSerialization.Serialize(expected), Context);
        await Assert.That(actual).IsEqualTo(expected);
        using var session = Context.Sessions.GetSession();
        await Assert.That(OrleansSerialization.Measure(expected, session)).IsEqualTo((long)OrleansSerialization.Serialize(expected).Length);
    }

    [Test]
    public async Task AcIs002InspectionRejectsWrongDynamicRootBeforeItsCodec()
    {
        var bytes = OrleansSerialization.Serialize(new QueueCounters(1, 1, 1, 1, 1));
        KeyLoadException? failure = null;
        try
        {
            _ = OrleansSerialization.Deserialize<OutboxHead>(bytes, Context);
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
    [Arguments(EnvelopeFault.DuplicateVersion)]
    [Arguments(EnvelopeFault.DuplicateValue)]
    [Arguments(EnvelopeFault.UnknownField)]
    public async Task AcIs002InspectionRejectsMalformedEnvelopeBeforeAdmission(EnvelopeFault fault)
    {
        using var fixture = new SerializerFixture();
        KeyLoadException? failure = null;
        try
        {
            _ = OrleansSerialization.Deserialize<OutboxHead>(fixture.EncodeEnvelope(fault), Context);
        }
        catch (KeyLoadException exception)
        {
            failure = exception;
        }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(PayloadVersion.InvalidPayload);
    }
}

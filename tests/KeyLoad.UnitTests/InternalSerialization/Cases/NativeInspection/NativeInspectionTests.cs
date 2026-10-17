using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeInspectionTests
{
    private static readonly NativeSerializerContext Context = NativeSerializerProviders.CreateInspection(typeof(OutboxHead));

    [Test]
    public async Task AcIs002InspectionUsesTheAuthoritativeNativeEnvelope()
    {
        var expected = new OutboxHead(1, 1, 1, 1);
        var actual = NativeSerialization.Deserialize<OutboxHead>(NativeSerialization.Serialize(expected), Context);
        await Assert.That(actual).IsEqualTo(expected);
        using var session = Context.Sessions.GetSession();
        await Assert.That(NativeSerialization.Measure(expected, session)).IsEqualTo((long)NativeSerialization.Serialize(expected).Length);
    }

    [Test]
    public async Task AcIs002InspectionRejectsWrongDynamicRootBeforeItsCodec()
    {
        var bytes = NativeSerialization.Serialize(new QueueCounters(1, 1, 1, 1, 1));
        KeyLoadException? failure = null;
        try
        {
            _ = NativeSerialization.Deserialize<OutboxHead>(bytes, Context);
        }
        catch (KeyLoadException exception)
        {
            failure = exception;
        }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    [Arguments(NativeEnvelopeFault.WrongRoot)]
    [Arguments(NativeEnvelopeFault.MissingVersion)]
    [Arguments(NativeEnvelopeFault.MissingValue)]
    [Arguments(NativeEnvelopeFault.DuplicateVersion)]
    [Arguments(NativeEnvelopeFault.DuplicateValue)]
    [Arguments(NativeEnvelopeFault.UnknownField)]
    public async Task AcIs002InspectionRejectsMalformedEnvelopeBeforeAdmission(NativeEnvelopeFault fault)
    {
        using var fixture = new NativeSerializerFixture();
        KeyLoadException? failure = null;
        try
        {
            _ = NativeSerialization.Deserialize<OutboxHead>(fixture.EncodeEnvelope(fault), Context);
        }
        catch (KeyLoadException exception)
        {
            failure = exception;
        }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(NativePayloadVersion.InvalidPayload);
    }
}

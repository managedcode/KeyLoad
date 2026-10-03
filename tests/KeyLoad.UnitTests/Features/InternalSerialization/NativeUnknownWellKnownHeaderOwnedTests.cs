using KeyLoad.Features.InternalSerialization;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeUnknownWellKnownHeaderOwnedTests
{
    private const string OwnedFailure = "Owned codec lookup invariant failed.";

    [Test]
    public async Task AcR17002OwnedCodecKeyNotFoundSurvivesSuccessfulPreflightWithoutNormalization()
    {
        var bytes = NativeUnknownWellKnownHeaderFixture.Encode(NativeUnknownWellKnownHeaderScope.Member, 1, malformed: false);
        NativeSerialization.Validate(bytes);
        var owned = new KeyNotFoundException(OwnedFailure);
        var context = NativeSerializerProviders.CreateInspection(typeof(NativeUnknownWellKnownHeaderRecord), builder =>
        {
            builder.Services.AddSingleton(new NativeUnknownWellKnownHeaderOwnedCodec(owned));
            builder.Configure(options => options.FieldCodecs.Add(typeof(NativeUnknownWellKnownHeaderOwnedCodec)));
        });
        var actual = Assert.ThrowsExactly<KeyNotFoundException>(() =>
            NativeSerialization.Deserialize<NativeUnknownWellKnownHeaderRecord>(bytes, context));
        await Assert.That(actual).IsSameReferenceAs(owned);
        await Assert.That(actual.Message).IsEqualTo(OwnedFailure);
        await Assert.That(NativeSerialization.Deserialize<NativeUnknownWellKnownHeaderRecord>(bytes).Number)
            .IsEqualTo(NativeUnknownWellKnownHeaderFixture.Number);
    }
}

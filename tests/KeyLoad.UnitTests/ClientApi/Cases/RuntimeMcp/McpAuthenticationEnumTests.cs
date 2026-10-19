using System.Buffers;
using KeyLoad.Clustering;
using KeyLoad.Server;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class McpAuthenticationEnumTests
{
    private const uint ScalarField = 0;

    [Test]
    public async Task ActualOfficialCapabilityCodecUsesBackingScalarMetadataAndRoundTrips()
    {
        var (actualType, value, remaining) = InspectOfficialCapability();
        await Assert.That(actualType).IsEqualTo(typeof(long));
        await Assert.That(value).IsEqualTo(Capability.All);
        await Assert.That(remaining).IsEqualTo(0);
        using var database = new TestDatabase();
        var principal = McpAuthenticationTests.Principal(database);
        var payload = OrleansSerialization.Serialize(new GrainValue(principal));
        var decoded = McpAuthentication.ReadPrincipal(payload, UnitMcpOptions.Execution(), CancellationToken.None);
        await Assert.That(JsonDefaults.Serialize(decoded).AsSpan().SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
    }

    private static (Type? ActualType, Capability Value, long Remaining) InspectOfficialCapability()
    {
        var context = SerializerProviders.Get(typeof(GrainValue));
        var output = new ArrayBufferWriter<byte>();
        using (var session = context.Sessions.GetSession())
        {
            var writer = Writer.Create(output, session);
            session.CodecProvider.GetCodec<Capability>().WriteField(ref writer, ScalarField, typeof(Capability), Capability.All);
            writer.Commit();
        }
        using var readSession = context.Sessions.GetSession();
        var reader = Reader.Create(output.WrittenSpan, readSession);
        var field = reader.ReadFieldHeader();
        var value = readSession.CodecProvider.GetCodec<Capability>().ReadValue(ref reader, field);
        return (field.FieldType, value, reader.Remaining);
    }
}

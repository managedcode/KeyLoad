using System.Buffers.Binary;
using KeyLoad.Features.InternalSerialization;
using KeyLoad.Replication;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.RecoveryTests;

internal static class ReplicaMaximumPayloadControlFixtures
{
    private const string DocumentJson = "{\"text\":\"Київ🌍\"}";

    internal static ReplicatedOperation Operation(ReplicaTermMetadataFixture fixture)
        => fixture.Operation(ReplicaTermMetadataFixture.AtomicBatch(DocumentJson));

    internal static byte[] Nested(ReplicaEntry entry, string shape)
    {
        var context = NativeSerializerProviders.CreateInspection(typeof(ReplicaEntry), builder =>
        {
            builder.AddAssembly(typeof(ReplicaEntry).Assembly);
            var operationCodec = new ReplicaMaximumOperationCodec(shape);
            builder.Services.AddSingleton(operationCodec);
            builder.Services.AddSingleton(new ReplicaTermMetadataEntryCodec(operationCodec));
            builder.Configure(options =>
            {
                options.FieldCodecs.Add(typeof(ReplicaTermMetadataEntryCodec));
                options.FieldCodecs.Add(typeof(ReplicaMaximumOperationCodec));
            });
        });
        var body = context.Serializer.SerializeToArray(new NativePayload { Version = NativePayloadVersion.Current, Value = entry });
        var bytes = new byte[checked(ReplicaProtocol.PayloadPrefixBytes + body.Length)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, ReplicaProtocol.PayloadMagic);
        body.CopyTo(bytes, ReplicaProtocol.PayloadPrefixBytes);
        return bytes;
    }

    internal static async Task AssertEquivalentAsync(ReplicatedOperation actual, ReplicatedOperation expected)
    {
        await Assert.That(actual.Id).IsEqualTo(expected.Id);
        await Assert.That(actual.Kind).IsEqualTo(expected.Kind);
        await Assert.That(actual.PrincipalId).IsEqualTo(expected.PrincipalId);
        await Assert.That(actual.EvaluatedAt).IsEqualTo(expected.EvaluatedAt);
        await Assert.That(actual.PayloadJson).IsEqualTo(expected.PayloadJson);
        await Assert.That(actual.NativePayload.Span.SequenceEqual(expected.NativePayload.Span)).IsTrue();
        await Assert.That(actual.NativePayload.IsEmpty).IsFalse();
    }
}

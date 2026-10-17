using System.Buffers.Binary;
using KeyLoad.Features.InternalSerialization;
using KeyLoad.Replication;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests;

/// <summary>Creates malformed native test payloads inside the current authoritative envelope.</summary>
internal static class ReplicaNativeFixtureEncoder
{
    internal static byte[] Encode<T, TCodec>(T value, TCodec codec) where TCodec : class
    {
        var context = NativeSerializerProviders.CreateInspection(typeof(T), builder =>
        {
            builder.AddAssembly(typeof(ReplicaEntry).Assembly);
            builder.Services.AddSingleton(codec);
            builder.Configure(options => options.FieldCodecs.Add(typeof(TCodec)));
        });
        var body = context.Serializer.SerializeToArray(new NativePayload { Version = NativePayloadVersion.Current, Value = value });
        var bytes = new byte[checked(ReplicaProtocol.PayloadPrefixBytes + body.Length)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, ReplicaProtocol.PayloadMagic);
        body.CopyTo(bytes, ReplicaProtocol.PayloadPrefixBytes);
        return bytes;
    }
}

using System.Buffers;
using System.Buffers.Binary;
using KeyLoad.Features.InternalSerialization;
using KeyLoad.Replication;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;

namespace KeyLoad.UnitTests;

// Official native writers intentionally author rejected field shapes inside the shared envelope.
internal static class ReplicaSecurityWireFixture
{
    internal static byte[] Operation(ReplicatedOperation value, string shape)
        => Encode(value, new ReplicaSecurityOperationCodec(shape));

    internal static byte[] Append(AppendRequest value, bool duplicateOperation)
        => Encode(value, new ReplicaSecurityAppendCodec(duplicateOperation), builder =>
        {
            if (duplicateOperation)
            {
                builder.Services.AddSingleton(new ReplicaSecurityEntryCodec());
                builder.Configure(options => options.FieldCodecs.Add(typeof(ReplicaSecurityEntryCodec)));
            }
        });

    private static byte[] Encode<T, TCodec>(T value, TCodec codec, Action<global::Orleans.Serialization.ISerializerBuilder>? configure = null)
        where TCodec : class
    {
        var context = NativeSerializerProviders.CreateInspection(typeof(T), builder =>
        {
            builder.AddAssembly(typeof(ReplicaEntry).Assembly);
            builder.Services.AddSingleton(codec);
            builder.Configure(options => options.FieldCodecs.Add(typeof(TCodec)));
            configure?.Invoke(builder);
        });
        var body = context.Serializer.SerializeToArray(new NativePayload { Version = NativePayloadVersion.Current, Value = value });
        var bytes = new byte[checked(ReplicaProtocol.PayloadPrefixBytes + body.Length)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, ReplicaProtocol.PayloadMagic);
        body.CopyTo(bytes, ReplicaProtocol.PayloadPrefixBytes);
        return bytes;
    }

    internal static void Write<T, TWriter>(ref Writer<TWriter> writer, uint delta, T value) where TWriter : IBufferWriter<byte>
        => writer.Session.CodecProvider.GetCodec<T>().WriteField(ref writer, delta, typeof(T), value);
}

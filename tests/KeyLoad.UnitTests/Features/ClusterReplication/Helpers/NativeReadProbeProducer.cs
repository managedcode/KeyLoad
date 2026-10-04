using System.Buffers;
using System.Buffers.Binary;
using KeyLoad.Features.InternalSerialization;
using KeyLoad.Replication;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;

namespace KeyLoad.UnitTests;

internal enum NativeReadProbeFault
{
    None, MissingTerm, MissingPreviousIndex, MissingPreviousTerm, MissingCommittedIndex,
    ZeroTerm, NegativePreviousIndex, ConflictingPreviousTerm, NegativeCommittedIndex,
    NullEntries, DuplicateEntries, UnknownEntries, MissingEntries, TrailingObject
}

// Genuine official native envelope and owning AppendRequest; the writer changes one strict field boundary.
internal static class NativeReadProbeProducer
{
    internal static byte[] Encode(AppendRequest value, NativeReadProbeFault fault)
    {
        var context = NativeSerializerProviders.CreateInspection(typeof(AppendRequest), builder =>
        {
            builder.AddAssembly(typeof(AppendRequest).Assembly);
            builder.Services.AddSingleton(new NativeReadProbeCodec(fault));
            builder.Configure(options => options.FieldCodecs.Add(typeof(NativeReadProbeCodec)));
        });
        var body = context.Serializer.SerializeToArray(new NativePayload { Version = NativePayloadVersion.Current, Value = value });
        if (fault == NativeReadProbeFault.TrailingObject)
        {
            // A second actual native object follows the otherwise complete current object.
            body = [.. body, .. body];
        }
        var bytes = new byte[checked(ReplicaProtocol.PayloadPrefixBytes + body.Length)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, ReplicaProtocol.PayloadMagic);
        body.CopyTo(bytes, ReplicaProtocol.PayloadPrefixBytes);
        return bytes;
    }

    internal static void Write<T, TWriter>(ref Writer<TWriter> writer, uint delta, T value) where TWriter : IBufferWriter<byte>
        => writer.Session.CodecProvider.GetCodec<T>().WriteField(ref writer, delta, typeof(T), value);
}

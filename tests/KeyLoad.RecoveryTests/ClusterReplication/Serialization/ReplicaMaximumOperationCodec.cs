using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaMaximumOperationCodec(string shape) : IFieldCodec<ReplicatedOperation>
{
    private readonly IFieldCodec<ReadOnlyMemory<byte>> bytesCodec = new ReadOnlyMemoryOfByteCodec();

    public ReplicatedOperation ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TWriter>(ref Writer<TWriter> writer, uint delta, [AllowNull] Type expectedType,
        [AllowNull] ReplicatedOperation value) where TWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, delta, expectedType, value))
        {
            return;
        }
        writer.WriteFieldHeader(delta, expectedType, typeof(ReplicatedOperation), WireType.TagDelimited);
        writer.WriteEndBase();
        Write(ref writer, 0, value.Id);
        Write(ref writer, 1, value.Kind);
        if (shape == ReplicaMaximumPayload.DuplicateField)
        {
            Write(ref writer, 0, value.Kind);
        }
        Write(ref writer, 1, value.PrincipalId);
        Write(ref writer, 1, value.EvaluatedAt);
        Write(ref writer, 1, value.PayloadJson);
        bytesCodec.WriteField(ref writer, 1, typeof(ReadOnlyMemory<byte>), value.NativePayload);
        if (shape == ReplicaMaximumPayload.UnknownField)
        {
            Write(ref writer, 1, true);
        }
        writer.WriteEndObject();
    }

    private static void Write<T, TWriter>(ref Writer<TWriter> writer, uint delta, T value) where TWriter : IBufferWriter<byte>
        => writer.Session.CodecProvider.GetCodec<T>().WriteField(ref writer, delta, typeof(T), value);
}

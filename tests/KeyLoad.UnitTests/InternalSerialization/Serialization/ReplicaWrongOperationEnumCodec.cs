using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class ReplicaWrongOperationEnumCodec(ReplicaWrongEnumScalar scalar) : IFieldCodec<ReplicatedOperation>
{
    public ReplicatedOperation ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta,
        [AllowNull] Type expectedType, [AllowNull] ReplicatedOperation value) where TBufferWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
        {
            return;
        }
        writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(ReplicatedOperation), WireType.TagDelimited);
        writer.WriteEndBase();
        writer.Session.CodecProvider.GetCodec<Guid>().WriteField(ref writer, 0, typeof(Guid), value.Id);
        ReplicaWrongEnumField.Write(ref writer, typeof(OperationKind), (int)value.Kind, scalar);
        writer.Session.CodecProvider.GetCodec<string>().WriteField(ref writer, 1, typeof(string), value.PrincipalId);
        writer.Session.CodecProvider.GetCodec<DateTimeOffset>().WriteField(ref writer, 1, typeof(DateTimeOffset), value.EvaluatedAt);
        writer.Session.CodecProvider.GetCodec<string>().WriteField(ref writer, 1, typeof(string), value.PayloadJson);
        writer.Session.CodecProvider.GetCodec<ReadOnlyMemory<byte>>().WriteField(ref writer, 1, typeof(ReadOnlyMemory<byte>), value.NativePayload);
        writer.WriteEndObject();
    }
}

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using KeyLoad.Core.Features.InternalSerialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class ReplicaWrongErrorEnumCodec(ReplicaWrongEnumScalar scalar) : IFieldCodec<NativeCommandPayload>
{
    public NativeCommandPayload ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta,
        [AllowNull] Type expectedType, [AllowNull] NativeCommandPayload value) where TBufferWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
        {
            return;
        }
        writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(NativeCommandPayload), WireType.TagDelimited);
        writer.WriteEndBase();
        writer.Session.CodecProvider.GetCodec<ReadOnlyMemory<byte>>().WriteField(ref writer, 0, typeof(ReadOnlyMemory<byte>), value.Value);
        ReplicaWrongEnumField.Write(ref writer, typeof(ErrorCode?), (int)value.Error!.Value, scalar);
        writer.Session.CodecProvider.GetCodec<string?>().WriteField(ref writer, 1, typeof(string), value.SafeDetail);
        writer.Session.CodecProvider.GetCodec<ReadOnlyMemory<byte>>().WriteField(ref writer,
            NativeCommandContract.AuthorityId - NativeCommandContract.SafeDetailId,
            typeof(ReadOnlyMemory<byte>), value.Authority);
        writer.Session.CodecProvider.GetCodec<ReadOnlyMemory<byte>>().WriteField(ref writer, 1, typeof(ReadOnlyMemory<byte>), value.Signature);
        writer.WriteEndObject();
    }
}

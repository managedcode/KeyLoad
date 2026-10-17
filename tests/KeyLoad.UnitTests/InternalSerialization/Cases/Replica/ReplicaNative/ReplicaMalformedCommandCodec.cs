using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using KeyLoad.Core.Features.InternalSerialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests;

internal sealed class ReplicaMalformedCommandCodec(ReplicaMalformedCommandShape shape) : IFieldCodec<NativeCommandPayload>
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
        writer.Session.CodecProvider.GetCodec<ErrorCode?>().WriteField(ref writer, 1, typeof(ErrorCode?), value.Error);
        StringCodec.WriteField(ref writer, 1, value.SafeDetail);
        if (shape == ReplicaMalformedCommandShape.WrongBodyReset)
        {
            writer.WriteEndBase();
        }
        if (shape != ReplicaMalformedCommandShape.MissingAuthority)
        {
            var delta = shape == ReplicaMalformedCommandShape.WrongBodyReset ? NativeCommandContract.AuthorityId
                : NativeCommandContract.AuthorityId - NativeCommandContract.SafeDetailId;
            writer.Session.CodecProvider.GetCodec<ReadOnlyMemory<byte>>().WriteField(ref writer, delta, typeof(ReadOnlyMemory<byte>), value.Authority);
        }
        var signatureDelta = shape == ReplicaMalformedCommandShape.MissingAuthority
            ? NativeCommandContract.SignatureId - NativeCommandContract.SafeDetailId : 1U;
        writer.Session.CodecProvider.GetCodec<ReadOnlyMemory<byte>>().WriteField(ref writer, signatureDelta, typeof(ReadOnlyMemory<byte>), value.Signature);
        if (shape == ReplicaMalformedCommandShape.UnknownBody)
        {
            BoolCodec.WriteField(ref writer, 1, true);
        }
        writer.WriteEndObject();
    }
}

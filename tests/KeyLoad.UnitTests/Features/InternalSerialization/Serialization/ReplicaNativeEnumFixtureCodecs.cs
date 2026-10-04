using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using KeyLoad.Core.Features.InternalSerialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal enum ReplicaWrongEnumScalar
{
    Int64,
    String
}

// Actual native producers preserve every field except the one deliberately wrong scalar metadata type.
internal static class ReplicaWrongEnumField
{
    private const string WrongValue = "invalid-enum-value";

    internal static void Write<TBufferWriter>(ref Writer<TBufferWriter> writer, Type expectedType,
        int value, ReplicaWrongEnumScalar scalar) where TBufferWriter : IBufferWriter<byte>
    {
        if (scalar == ReplicaWrongEnumScalar.Int64)
        {
            writer.Session.CodecProvider.GetCodec<long>().WriteField(ref writer, 1, expectedType, value);
        }
        else
        {
            writer.Session.CodecProvider.GetCodec<string>().WriteField(ref writer, 1, expectedType, WrongValue);
        }
    }
}

internal sealed class ReplicaWrongOperationEnumCodec(ReplicaWrongEnumScalar scalar) : IFieldCodec<ReplicatedOperation>
{
    public ReplicatedOperation ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta,
        [AllowNull] Type expectedType, [AllowNull] ReplicatedOperation value) where TBufferWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
        { return; }
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

internal sealed class ReplicaWrongErrorEnumCodec(ReplicaWrongEnumScalar scalar) : IFieldCodec<NativeCommandPayload>
{
    public NativeCommandPayload ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta,
        [AllowNull] Type expectedType, [AllowNull] NativeCommandPayload value) where TBufferWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
        { return; }
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

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.Features.InternalSerialization;

// Admission alone requires the exact current envelope. Ordinary persisted fields retain native
// compatible-field evolution. Counting writes delegate to the official generated envelope codec.
internal sealed class NativePayloadInspectionCodec(Type expectedRoot) : IFieldCodec<NativePayload>
{
    private static readonly IFieldCodec<NativePayload> WriteCodec = NativeSerializerProviders.Get(typeof(NativePayload))
        .Sessions.CodecProvider.GetCodec<NativePayload>();

    public NativePayload ReadValue<TInput>(ref Reader<TInput> reader, Field field)
    {
        NativePayloadHeader.Validate(field);
        var reference = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
        var versionField = reader.ReadFieldHeader();
        RequireField(versionField, 0, typeof(uint));
        var version = UInt32Codec.ReadValue(ref reader, versionField);
        if (version != NativePayloadVersion.Current)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, NativePayloadVersion.UnsupportedVersion);
        }
        var valueField = reader.ReadFieldHeader();
        RequireField(valueField, 1, null);
        if (valueField.FieldType != expectedRoot || valueField.IsReference)
        {
            throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
        }
        var value = ObjectCodec.ReadValue(ref reader, valueField);
        if (!reader.ReadFieldHeader().IsEndObject || value is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
        }
        var payload = new NativePayload { Version = version, Value = value };
        ReferenceCodec.RecordObject(reader.Session, payload, reference);
        return payload;
    }

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta,
        [AllowNull] Type expectedType, [AllowNull] NativePayload value) where TBufferWriter : IBufferWriter<byte>
        => WriteCodec.WriteField(ref writer, fieldIdDelta, expectedType, value);

    private static void RequireField(Field field, uint delta, Type? expectedType)
    {
        if (!field.HasFieldId || field.FieldIdDelta != delta
            || expectedType is not null && field.FieldType is not null && field.FieldType != expectedType)
        {
            throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
        }
    }
}

internal static class NativePayloadHeader
{
    internal static void Validate(Field field)
    {
        if (!field.HasFieldId || field.FieldIdDelta != 0 || field.WireType != WireType.TagDelimited
            || field.FieldType is not null && field.FieldType != typeof(NativePayload))
        {
            throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
        }
    }
}

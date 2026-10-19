using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.Features.InternalSerialization;

// Admission alone requires the exact current envelope. Ordinary persisted fields retain execution
// compatible-field evolution. Counting writes delegate to the official generated envelope codec.
internal sealed class PayloadInspectionCodec(Type expectedRoot) : IFieldCodec<RuntimePayload>
{
    private static readonly IFieldCodec<RuntimePayload> WriteCodec = SerializerProviders.Get(typeof(RuntimePayload))
        .Sessions.CodecProvider.GetCodec<RuntimePayload>();

    public RuntimePayload ReadValue<TInput>(ref Reader<TInput> reader, Field field)
    {
        PayloadHeader.Validate(field);
        var reference = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
        var versionField = reader.ReadFieldHeader();
        RequireField(versionField, WireIdentities.VersionFieldDelta, typeof(uint));
        var version = UInt32Codec.ReadValue(ref reader, versionField);
        if (version != PayloadVersion.Current)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, PayloadVersion.UnsupportedVersion);
        }
        var valueField = reader.ReadFieldHeader();
        RequireField(valueField, WireIdentities.ValueFieldDelta, null);
        if (valueField.FieldType != expectedRoot || valueField.IsReference)
        {
            throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
        }
        var value = ObjectCodec.ReadValue(ref reader, valueField);
        if (!reader.ReadFieldHeader().IsEndObject || value is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
        }
        var payload = new RuntimePayload { Version = version, Value = value };
        ReferenceCodec.RecordObject(reader.Session, payload, reference);
        return payload;
    }

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta,
        [AllowNull] Type expectedType, [AllowNull] RuntimePayload value) where TBufferWriter : IBufferWriter<byte>
        => WriteCodec.WriteField(ref writer, fieldIdDelta, expectedType, value);

    private static void RequireField(Field field, uint delta, Type? expectedType)
    {
        if (!field.HasFieldId || field.FieldIdDelta != delta
            || expectedType is not null && field.FieldType is not null && field.FieldType != expectedType)
        {
            throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
        }
    }
}

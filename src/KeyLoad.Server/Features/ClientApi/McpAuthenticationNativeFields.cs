using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.Server;

internal static class McpAuthenticationNativeFields
{
    internal static Field Field<TInput>(ref Reader<TInput> reader, uint delta, Type expected)
    {
        var field = reader.ReadFieldHeader();
        Require(field.HasFieldId && field.FieldIdDelta == delta
            && (field.FieldType is null || field.FieldType == expected));
        return field;
    }

    internal static T Scalar<T, TInput>(ref Reader<TInput> reader, uint delta)
    {
        // The official NullableCodec delegates present values to the underlying scalar codec.
        var expected = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        // Current ungenerated Capability uses Orleans10.3.1 backing-scalar metadata.
        if (expected == typeof(Capability))
        { expected = typeof(long); }
        return reader.Session.CodecProvider.GetCodec<T>().ReadValue(ref reader, Field(ref reader, delta, expected))!;
    }

    internal static uint Begin<TInput>(ref Reader<TInput> reader, Field field, Type expected)
    {
        Require(!field.IsReference && field.WireType == WireType.TagDelimited
            && (field.FieldType is null || field.FieldType == expected));
        return ReferenceCodec.CreateRecordPlaceholder(reader.Session);
    }

    internal static McpAuthenticationReference? Reference<TInput>(ref Reader<TInput> reader, Type expected)
    {
        ReferenceCodec.MarkValueField(reader.Session);
        var reference = reader.ReadVarUInt32();
        if (reference == 0)
        { return null; }
        var value = reader.Session.ReferencedObjects.TryGetReferencedObject(reference) as McpAuthenticationReference;
        Require(value is not null && value.NativeType == expected);
        return value;
    }

    internal static void Record<TInput>(ref Reader<TInput> reader, uint reference, Type type, McpFrameShape shape)
        => ReferenceCodec.RecordObject(reader.Session, new McpAuthenticationReference(type, shape), reference);

    internal static void End<TInput>(ref Reader<TInput> reader) => Require(reader.ReadFieldHeader().IsEndObject);
    internal static void EndBase<TInput>(ref Reader<TInput> reader) => Require(reader.ReadFieldHeader().IsEndBaseFields);
    internal static void Require(bool condition)
    {
        if (!condition)
        { throw Errors.Fail(ErrorCode.Validation, McpNativeAuthentication.InvalidReply); }
    }
}

using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Session;

namespace KeyLoad.Features.InternalSerialization;

// Official native headers/scalars are inspected before generated decoding allocates values.
internal static class NativePayloadSyntax
{
    internal static void Validate(ReadOnlySpan<byte> bytes, SerializerSession session, Type? expectedRootType = null, bool exactRoot = false)
    {
        var reader = Reader.Create(bytes, session);
        NativePayloadHeader.Validate(reader.ReadFieldHeader());
        var version = reader.ReadFieldHeader();
        NativeWireCheck.Require(version.HasFieldId && version.FieldIdDelta == 0
            && (version.FieldType is null || version.FieldType == typeof(uint)));
        if (UInt32Codec.ReadValue(ref reader, version) != NativePayloadVersion.Current)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, NativePayloadVersion.UnsupportedVersion);
        }
        var value = reader.ReadFieldHeader();
        NativeWireCheck.Require(value.HasFieldId && value.FieldIdDelta == 1 && value.FieldType is not null && !value.IsReference);
        NativeWireCheck.Require(expectedRootType is null || (exactRoot ? value.FieldType == expectedRootType
            : NativeWireSchema.Compatible(expectedRootType, value.FieldType!)));
        NativeWireWalk.Validate(ref reader, value);
        NativeWireCheck.Require(reader.ReadFieldHeader().IsEndObject && reader.Remaining == 0);
    }
}

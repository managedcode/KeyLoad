using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Session;

namespace KeyLoad.Features.InternalSerialization;

// Official native headers/scalars are inspected before generated decoding allocates values.
internal static class NativePayloadSyntax
{
    // Orleans 10.3.1 Reader uses this distinct throw site for exhausted/truncated buffers.
    // Other InvalidOperationException failures, including session/codec invariants, must escape.
    internal static bool IsReaderBufferFailure(Exception exception)
    {
        if (exception is not InvalidOperationException)
        {
            return false;
        }
        var method = exception.TargetSite;
        return exception.Message == "Insufficient data present in buffer."
            && method?.Name == "ThrowInsufficientData"
            && method.DeclaringType is { IsGenericType: true } declaring
            && declaring.GetGenericTypeDefinition() == typeof(Reader<>);
    }

    internal static void Validate(ReadOnlySpan<byte> bytes, SerializerSession session, Type? expectedRootType = null, bool exactRoot = false)
    {
        var reader = Reader.Create(bytes, session);
        NativePayloadHeader.Validate(NativeFieldHeaderReader.Read(ref reader));
        var version = NativeFieldHeaderReader.Read(ref reader);
        NativeWireCheck.Require(version.HasFieldId && version.FieldIdDelta == 0
            && (version.FieldType is null || version.FieldType == typeof(uint)));
        if (UInt32Codec.ReadValue(ref reader, version) != NativePayloadVersion.Current)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, NativePayloadVersion.UnsupportedVersion);
        }
        var value = NativeFieldHeaderReader.Read(ref reader);
        NativeWireCheck.Require(value.HasFieldId && value.FieldIdDelta == 1 && value.FieldType is not null && !value.IsReference);
        NativeWireCheck.Require(expectedRootType is null || (exactRoot ? value.FieldType == expectedRootType
            : NativeWireSchema.Compatible(expectedRootType, value.FieldType!)));
        NativeWireWalk.Validate(ref reader, value);
        NativeWireCheck.Require(NativeFieldHeaderReader.Read(ref reader).IsEndObject && reader.Remaining == 0);
    }
}

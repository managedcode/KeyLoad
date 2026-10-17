using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Session;

namespace KeyLoad.Features.InternalSerialization;

// Official native headers/scalars are inspected before generated decoding allocates values.
internal static class NativePayloadSyntax
{
    private const string ReaderInsufficientDataMessage = "Insufficient data present in buffer.";
    private const string ReaderInsufficientDataMethod = "ThrowInsufficientData";

    // Orleans 10.3.1 Reader uses this distinct throw site for exhausted/truncated buffers.
    // Other InvalidOperationException failures, including session/codec invariants, must escape.
    internal static bool IsReaderBufferFailure(Exception exception)
    {
        if (exception is not InvalidOperationException)
        {
            return false;
        }
        var method = exception.TargetSite;
        return exception.Message == ReaderInsufficientDataMessage
            && method?.Name == ReaderInsufficientDataMethod
            && method.DeclaringType is { IsGenericType: true } declaring
            && declaring.GetGenericTypeDefinition() == typeof(Reader<>);
    }

    internal static void Validate(ReadOnlySpan<byte> bytes, SerializerSession session, Type? expectedRootType = null, bool exactRoot = false)
    {
        var reader = Reader.Create(bytes, session);
        NativePayloadHeader.Validate(NativeFieldHeaderReader.Read(ref reader));
        var version = NativeFieldHeaderReader.Read(ref reader);
        NativeWireCheck.Require(version.HasFieldId && version.FieldIdDelta == NativeWireIdentities.VersionFieldDelta
            && (version.FieldType is null || version.FieldType == typeof(uint)));
        if (UInt32Codec.ReadValue(ref reader, version) != NativePayloadVersion.Current)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, NativePayloadVersion.UnsupportedVersion);
        }
        var value = NativeFieldHeaderReader.Read(ref reader);
        NativeWireCheck.Require(value.HasFieldId && value.FieldIdDelta == NativeWireIdentities.ValueFieldDelta && value.FieldType is not null && !value.IsReference);
        NativeWireCheck.Require(expectedRootType is null || (exactRoot ? value.FieldType == expectedRootType
            : NativeWireSchema.Compatible(expectedRootType, value.FieldType!)));
        NativeWireWalk.Validate(ref reader, value);
        NativeWireCheck.Require(NativeFieldHeaderReader.Read(ref reader).IsEndObject && reader.Remaining == NativeWireIdentities.EmptyRemainingBytes);
    }
}

using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Session;

namespace KeyLoad.Features.InternalSerialization;

// Official execution headers/scalars are inspected before generated decoding allocates values.
internal static class RuntimePayloadSyntax
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
        PayloadHeader.Validate(FieldHeaderReader.Read(ref reader));
        var version = FieldHeaderReader.Read(ref reader);
        WireCheck.Require(version.HasFieldId && version.FieldIdDelta == WireIdentities.VersionFieldDelta
            && (version.FieldType is null || version.FieldType == typeof(uint)));
        if (UInt32Codec.ReadValue(ref reader, version) != PayloadVersion.Current)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, PayloadVersion.UnsupportedVersion);
        }
        var value = FieldHeaderReader.Read(ref reader);
        WireCheck.Require(value.HasFieldId && value.FieldIdDelta == WireIdentities.ValueFieldDelta && value.FieldType is not null && !value.IsReference);
        WireCheck.Require(expectedRootType is null || (exactRoot ? value.FieldType == expectedRootType
            : WireSchema.Compatible(expectedRootType, value.FieldType!)));
        WireWalk.Validate(ref reader, value);
        WireCheck.Require(FieldHeaderReader.Read(ref reader).IsEndObject && reader.Remaining == WireIdentities.EmptyRemainingBytes);
    }
}

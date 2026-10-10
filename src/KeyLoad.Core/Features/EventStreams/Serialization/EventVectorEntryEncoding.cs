using System.Collections.Immutable;
using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorEntryEncoding
{
    private const string InvalidEntries = "The original native event vector entry payload is inconsistent.";

    internal static ReadOnlyMemory<byte> Encode(ImmutableArray<EventVectorEntry> entries,
        EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        if (entries.IsDefault)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidEntries); }
        admission.RequireEntryCount(entries.Length);
        admission.RequireEncodedBytes(NativeSerialization.Measure(entries));
        var bytes = NativeSerialization.Serialize(entries);
        admission.RequireEncodedBytes(bytes.LongLength);
        return bytes;
    }

    internal static ImmutableArray<EventVectorEntry> Decode(ReadOnlyMemory<byte> original,
        ReadOnlyMemory<byte> checksum, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        admission.RequireEncodedBytes(original.Length);
        var actual = SHA256.HashData(original.Span);
        if (original.IsEmpty || !CryptographicOperations.FixedTimeEquals(actual, checksum.Span))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidEntries); }
        var entries = NativeSerialization.Deserialize<ImmutableArray<EventVectorEntry>>(original.Span);
        if (entries.IsDefault)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidEntries); }
        admission.RequireEntryCount(entries.Length);
        if (entries.Any(static entry => entry is null || entry.Source is null
                || entry.Source.Partition is null || string.IsNullOrWhiteSpace(entry.Source.Resource)))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidEntries); }
        return entries;
    }

    internal static ReadOnlyMemory<byte> Digest(ReadOnlyMemory<byte> original)
        => SHA256.HashData(original.Span);
}

using System.Security.Cryptography;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>The original recovery decoder reads one complete native frame without modifying the stream.</summary>
internal static class ZoneTreeJournalFrameReader
{
    internal static (byte[] Payload, long Sequence, byte[] Checksum)? ReadComplete(FileStream journal,
        byte[] header, long position, int maxFrameBytes, int identityVersion)
    {
        var fields = ReadHeader(journal, header, position, maxFrameBytes, identityVersion);
        return fields is { } actual ? ReadPayload(journal, header, actual.Length, actual.Sequence) : null;
    }

    internal static (int Length, long Sequence)? ReadHeader(FileStream journal, byte[] header,
        long position, int maxFrameBytes, int identityVersion)
    {
        if (journal.Length - journal.Position < HeaderLength)
        { return null; }
        journal.ReadExactly(header);
        return ZoneTreeJournalRecovery.ValidateHeader(header, position, maxFrameBytes, identityVersion);
    }

    internal static (byte[] Payload, long Sequence, byte[] Checksum)? ReadPayload(FileStream journal,
        byte[] header, int length, long sequence)
    {
        if (journal.Length - journal.Position < length)
        { return null; }
        var payload = new byte[length];
        journal.ReadExactly(payload);
        var checksum = SHA256.HashData(payload);
        if (!CryptographicOperations.FixedTimeEquals(checksum, header.AsSpan(ChecksumOffset, ChecksumLength)))
        { throw Errors.Fail(ErrorCode.Corruption, JournalChecksumInvalid); }
        return (payload, sequence, checksum);
    }
}

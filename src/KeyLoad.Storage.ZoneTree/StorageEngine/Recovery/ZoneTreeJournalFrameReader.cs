using System.Security.Cryptography;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>The original recovery decoder reads one complete execution frame without modifying the stream.</summary>
internal static class ZoneTreeJournalFrameReader
{
    internal static (bool HasValue, (byte[] Payload, long Sequence, byte[] Checksum)? Value) ReadComplete(FileStream journal,
        byte[] header, long position, int maxFrameBytes, int identityVersion)
    {
        var fields = (ReadHeader(journal, header, position, maxFrameBytes, identityVersion)).Value;
        return (((byte[] Payload, long Sequence, byte[] Checksum)?)(fields is { } actual ? (ReadPayload(journal, header, actual.Length, actual.Sequence)).Value : null)) is { } outcomeValue1 ? (true, outcomeValue1) : (false, default((byte[] Payload, long Sequence, byte[] Checksum)?));
    }

    internal static (bool HasValue, (int Length, long Sequence)? Value) ReadHeader(FileStream journal, byte[] header,
        long position, int maxFrameBytes, int identityVersion)
    {
        if (journal.Length - journal.Position < HeaderLength)
        {
            return (false, default((int Length, long Sequence)?));
        }
        journal.ReadExactly(header);
        return (true, (((int Length, long Sequence)?)(ZoneTreeJournalRecovery.ValidateHeader(header, position, maxFrameBytes, identityVersion))));
    }

    internal static (bool HasValue, (byte[] Payload, long Sequence, byte[] Checksum)? Value) ReadPayload(FileStream journal,
        byte[] header, int length, long sequence)
    {
        if (journal.Length - journal.Position < length)
        {
            return (false, default((byte[] Payload, long Sequence, byte[] Checksum)?));
        }
        var payload = new byte[length];
        journal.ReadExactly(payload);
        var checksum = SHA256.HashData(payload);
        if (!CryptographicOperations.FixedTimeEquals(checksum, header.AsSpan(ChecksumOffset, ChecksumLength)))
        {
            throw Errors.Fail(ErrorCode.Corruption, JournalChecksumInvalid);
        }
        return (true, (((byte[] Payload, long Sequence, byte[] Checksum)?)((payload, sequence, checksum))));
    }
}

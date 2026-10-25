using System.Security.Cryptography;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>The original recovery decoder reads one complete execution frame without modifying the stream.</summary>
internal static class ZoneTreeJournalFrameReader
{
    internal static global::ManagedCode.Communication.Result<(byte[] Payload, long Sequence, byte[] Checksum)?> ReadComplete(FileStream journal,
        byte[] header, long position, int maxFrameBytes, int identityVersion)
    {
        var fields = (ReadHeader(journal, header, position, maxFrameBytes, identityVersion)).Value;
        return (((byte[] Payload, long Sequence, byte[] Checksum)?)(fields is { } actual ? (ReadPayload(journal, header, actual.Length, actual.Sequence)).Value : null)) is { } outcomeValue1 ? global::ManagedCode.Communication.Result<(byte[] Payload, long Sequence, byte[] Checksum)?>.Succeed(outcomeValue1) : global::ManagedCode.Communication.Result<(byte[] Payload, long Sequence, byte[] Checksum)?>.FailNotFound(nameof(ReadComplete));
    }

    internal static global::ManagedCode.Communication.Result<(int Length, long Sequence)?> ReadHeader(FileStream journal, byte[] header,
        long position, int maxFrameBytes, int identityVersion)
    {
        if (journal.Length - journal.Position < HeaderLength)
        {
            return global::ManagedCode.Communication.Result<(int Length, long Sequence)?>.FailNotFound(nameof(ReadHeader));
        }
        journal.ReadExactly(header);
        return global::ManagedCode.Communication.Result<(int Length, long Sequence)?>.Succeed((((int Length, long Sequence)?)(ZoneTreeJournalRecovery.ValidateHeader(header, position, maxFrameBytes, identityVersion))));
    }

    internal static global::ManagedCode.Communication.Result<(byte[] Payload, long Sequence, byte[] Checksum)?> ReadPayload(FileStream journal,
        byte[] header, int length, long sequence)
    {
        if (journal.Length - journal.Position < length)
        {
            return global::ManagedCode.Communication.Result<(byte[] Payload, long Sequence, byte[] Checksum)?>.FailNotFound(nameof(ReadPayload));
        }
        var payload = new byte[length];
        journal.ReadExactly(payload);
        var checksum = SHA256.HashData(payload);
        if (!CryptographicOperations.FixedTimeEquals(checksum, header.AsSpan(ChecksumOffset, ChecksumLength)))
        {
            throw Errors.Fail(ErrorCode.Corruption, JournalChecksumInvalid);
        }
        return global::ManagedCode.Communication.Result<(byte[] Payload, long Sequence, byte[] Checksum)?>.Succeed((((byte[] Payload, long Sequence, byte[] Checksum)?)((payload, sequence, checksum))));
    }
}

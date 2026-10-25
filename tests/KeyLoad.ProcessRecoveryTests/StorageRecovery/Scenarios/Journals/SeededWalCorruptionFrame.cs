using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KeyLoad.ProcessRecoveryTests.StorageRecovery;

internal static class SeededWalCorruptionFrame
{
    private const ulong CurrentJournalMagic = 0x344C4157444C4BUL;
    private const int HeaderBytes = 52;
    private const int PayloadLengthOffset = 8;
    private const int SequenceOffset = 12;
    private const int ChecksumOffset = 20;
    private const int ChecksumBytes = 32;
    private const string Invalid = "The original seeded execution WAL is not a complete current checksummed frame history.";
    internal static long Validate(byte[] journal)
    {
        var offset = 0;
        long sequence = 0;
        while (offset < journal.Length)
        {
            if (journal.Length - offset < HeaderBytes)
            {
                throw new InvalidOperationException(Invalid);
            }
            var header = journal.AsSpan(offset, HeaderBytes);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header[PayloadLengthOffset..]);
            if (BinaryPrimitives.ReadUInt64LittleEndian(header) != CurrentJournalMagic || length <= 0
                || length > journal.Length - offset - HeaderBytes
                || BinaryPrimitives.ReadInt64LittleEndian(header[SequenceOffset..]) != sequence + 1)
            {
                throw new InvalidOperationException(Invalid);
            }
            var payload = journal.AsSpan(offset + HeaderBytes, length);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), header.Slice(ChecksumOffset, ChecksumBytes)))
            {
                throw new InvalidOperationException(Invalid);
            }
            sequence++;
            offset = checked(offset + HeaderBytes + length);
        }
        if (sequence == 0)
        {
            throw new InvalidOperationException(Invalid);
        }
        return sequence;
    }
}

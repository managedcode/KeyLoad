using System.Buffers.Binary;
using System.Security.Cryptography;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

// Verify the whole recoverable prefix before opening a provider which can rewrite its metadata.
// The caller holds the owner lock and retains this same journal handle through ordered recovery.
internal static class ZoneTreeJournalPreflight
{
    internal static void Validate(FileStream journal, ZoneTreeStoreOptions options, int identityVersion)
    {
        try
        {
            journal.Position = 0;
            var header = new byte[HeaderLength];
            var position = ReadCheckpoint(journal, options, header);
            while (journal.Length - journal.Position >= HeaderLength)
            {
                if (!ReadFrame(journal, options, identityVersion, header, ref position))
                {
                    break;
                }
            }
        }
        finally
        {
            // Only ordinary recovery applies verified records and truncates an incomplete current tail.
            journal.Position = 0;
        }
    }

    private static long ReadCheckpoint(FileStream journal, ZoneTreeStoreOptions options, byte[] header)
    {
        if (journal.Length < HeaderLength)
        {
            return 0;
        }
        journal.ReadExactly(header);
        journal.Position = 0;
        var magic = BinaryPrimitives.ReadUInt64LittleEndian(header);
        if (magic is SourceCheckpointMagic or Native6CheckpointMagic)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, JournalFormatUpgradeRequired);
        }
        if (magic != CheckpointMagic)
        {
            return 0;
        }
        // Normal recovery also supplies an apply callback. A no-op preserves those same bounds;
        // the complete journal may exceed the standalone snapshot limit because it contains a tail.
        return ZoneTreeCheckpointReader.Read(journal, options, static _ => { }).Position;
    }

    private static bool ReadFrame(FileStream journal, ZoneTreeStoreOptions options, int identityVersion,
        byte[] header, ref long position)
    {
        journal.ReadExactly(header);
        var (length, sequence) = ZoneTreeJournalRecovery.ValidateHeader(header, position, options.MaxFrameBytes, identityVersion);
        if (journal.Length - journal.Position < length)
        {
            return false;
        }
        var payload = new byte[length];
        journal.ReadExactly(payload);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), header.AsSpan(ChecksumOffset, ChecksumLength)))
        {
            throw Errors.Fail(ErrorCode.Corruption, JournalChecksumInvalid);
        }
        _ = ZoneTreeJournalCodec.Deserialize(payload);
        position = sequence;
        return true;
    }
}

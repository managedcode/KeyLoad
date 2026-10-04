using System.Buffers.Binary;
using System.Security.Cryptography;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeBackupJournalValidation
{
    private const string JournalIncomplete = "The backup redo journal is incomplete.";
    private const string ManifestCutMismatch = "The backup journal does not match its manifest cut.";

    internal static void Verify(FileStream journal, StoreIdentity identity, long expectedPosition)
    {
        var options = new ZoneTreeStoreOptions(Path.GetDirectoryName(journal.Name)!);
        var header = new byte[HeaderLength];
        var position = ReadCheckpoint(journal, header, options);
        while (journal.Position < journal.Length)
        {
            if (journal.Length - journal.Position < HeaderLength)
            {
                throw Errors.Fail(ErrorCode.Corruption, JournalIncomplete);
            }
            journal.ReadExactly(header);
            var (length, sequence) = ZoneTreeJournalRecovery.ValidateHeader(header, position,
                options.MaxFrameBytes, identity.FormatVersion);
            if (journal.Length - journal.Position < length)
            {
                throw Errors.Fail(ErrorCode.Corruption, JournalIncomplete);
            }
            var payload = new byte[length];
            journal.ReadExactly(payload);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload),
                header.AsSpan(ChecksumOffset, ChecksumLength)))
            {
                throw Errors.Fail(ErrorCode.Corruption, JournalChecksumInvalid);
            }
            ZoneTreeJournalCodec.Deserialize(payload);
            position = sequence;
        }
        if (position != expectedPosition)
        {
            throw Errors.Fail(ErrorCode.Corruption, ManifestCutMismatch);
        }
    }

    private static long ReadCheckpoint(FileStream journal, byte[] header, ZoneTreeStoreOptions options)
    {
        if (journal.Length < HeaderLength)
        {
            return 0;
        }
        journal.ReadExactly(header);
        journal.Position = 0;
        // Recovery bounds each frame; a compacted checkpoint can have later WAL frames.
        // The observer validates every record without materializing a tree or imposing
        // the standalone-snapshot whole-file size limit on the combined journal.
        var magic = BinaryPrimitives.ReadUInt64LittleEndian(header);
        if (magic == SourceCheckpointMagic)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, JournalFormatUpgradeRequired);
        }
        return magic == CheckpointMagic
            ? ZoneTreeCheckpointReader.Read(journal, options, ObserveValidatedMutation).Position
            : 0;
    }

    private static void ObserveValidatedMutation(StorageMutation mutation) => _ = mutation;
}

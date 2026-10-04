using System.Buffers.Binary;
using System.Security.Cryptography;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeJournalRecovery
{
    internal static void Recover(ZoneTreeStoreRuntime runtime)
    {
        var header = new byte[HeaderLength];
        var validLength = RecoverCheckpoint(runtime, header);
        while (runtime.Journal.Position < runtime.Journal.Length)
        {
            if (!ReplayFrame(runtime, header, ref validLength))
            {
                break;
            }
        }

        runtime.Journal.Position = runtime.Journal.Length;
        runtime.Journal.Flush(true);
    }

    private static long RecoverCheckpoint(ZoneTreeStoreRuntime runtime, byte[] header)
    {
        if (runtime.Journal.Length < HeaderLength)
        {
            return 0;
        }

        runtime.Journal.ReadExactly(header);
        runtime.Journal.Position = 0;
        var magic = BinaryPrimitives.ReadUInt64LittleEndian(header);
        if (magic is SourceCheckpointMagic or Native6CheckpointMagic)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, JournalFormatUpgradeRequired);
        }
        if (magic != CheckpointMagic)
        {
            return 0;
        }

        var checkpoint = ZoneTreeCheckpointReader.Read(runtime.Journal, runtime.Options, runtime.Apply);
        runtime.SetPosition(checkpoint.Position);
        return runtime.Journal.Position;
    }

    private static bool ReplayFrame(ZoneTreeStoreRuntime runtime, byte[] header, ref long validLength)
    {
        var journal = runtime.Journal;
        if (journal.Length - journal.Position < HeaderLength)
        {
            journal.SetLength(validLength);
            return false;
        }

        journal.ReadExactly(header);
        var (length, sequence) = ValidateHeader(header, runtime.Position, runtime.Options.MaxFrameBytes, runtime.Identity.FormatVersion);
        if (journal.Length - journal.Position < length)
        {
            journal.SetLength(validLength);
            return false;
        }

        var payload = new byte[length];
        journal.ReadExactly(payload);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), header.AsSpan(ChecksumOffset, ChecksumLength)))
        {
            throw Errors.Fail(ErrorCode.Corruption, JournalChecksumInvalid);
        }

        foreach (var mutation in ZoneTreeJournalCodec.Deserialize(payload))
        {
            runtime.Apply(mutation);
        }

        runtime.SetPosition(sequence);
        validLength = journal.Position;
        return true;
    }

    internal static (int Length, long Sequence) ValidateHeader(byte[] header, long position, int maxFrameBytes, int identityVersion)
    {
        var magic = BinaryPrimitives.ReadUInt64LittleEndian(header);
        if (magic is LegacyJournalMagic or LegacyBinaryJournalMagic or LegacyNativeMutationJournalMagic
            || magic == JournalMagic && identityVersion != CurrentDataEpoch)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, JournalFormatUpgradeRequired);
        }

        if (magic != JournalMagic)
        {
            throw Errors.Fail(ErrorCode.Corruption, JournalHeaderInvalid);
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(PayloadLengthOffset));
        var sequence = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(SequenceOffset));
        if (length <= 0 || length > maxFrameBytes || position == long.MaxValue
            || sequence != position + 1)
        {
            throw Errors.Fail(ErrorCode.Corruption, JournalSequenceInvalid);
        }

        return (length, sequence);
    }
}

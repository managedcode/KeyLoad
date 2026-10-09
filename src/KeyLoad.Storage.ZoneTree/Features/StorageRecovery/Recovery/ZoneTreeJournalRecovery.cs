using System.Buffers.Binary;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeJournalRecovery
{
    private const int InitialJournalPosition = 0;
    private const int FileStartPosition = 0;
    private const int EmptyFrameLength = 0;
    private const int NextJournalSequenceIncrement = 1;

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
            return InitialJournalPosition;
        }

        runtime.Journal.ReadExactly(header);
        runtime.Journal.Position = FileStartPosition;
        var magic = BinaryPrimitives.ReadUInt64LittleEndian(header);
        if (magic != CheckpointMagic)
        {
            return InitialJournalPosition;
        }

        var checkpoint = ZoneTreeCheckpointReader.Read(runtime.Journal, runtime.Options, runtime.Apply);
        runtime.SetPosition(checkpoint.Position);
        return runtime.Journal.Position;
    }

    private static bool ReplayFrame(ZoneTreeStoreRuntime runtime, byte[] header, ref long validLength)
    {
        var journal = runtime.Journal;
        var frame = ZoneTreeJournalFrameReader.ReadComplete(journal, header, runtime.Position,
            runtime.Options.MaxFrameBytes, runtime.Identity.FormatVersion);
        if (frame is null)
        {
            journal.SetLength(validLength);
            return false;
        }
        var (payload, sequence, _) = frame.Value;

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
        if (magic == JournalMagic && identityVersion != CurrentDataEpoch)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, JournalFormatUnsupported);
        }

        if (magic != JournalMagic)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, JournalFormatUnsupported);
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(PayloadLengthOffset));
        var sequence = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(SequenceOffset));
        if (length <= EmptyFrameLength || length > maxFrameBytes || position == long.MaxValue
            || sequence != position + NextJournalSequenceIncrement)
        {
            throw Errors.Fail(ErrorCode.Corruption, JournalSequenceInvalid);
        }

        return (length, sequence);
    }
}

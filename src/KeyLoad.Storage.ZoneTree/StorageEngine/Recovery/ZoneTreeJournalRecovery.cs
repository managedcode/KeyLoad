using System.Buffers.Binary;
using ZoneTree.Transactional;
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
        if (magic != CheckpointMagic)
        {
            return 0;
        }

        using var transaction = runtime.Tree.BeginTransaction();
        RemoveCheckpointBaseline(runtime, transaction);
        var checkpoint = ZoneTreeCheckpointReader.Read(runtime.Journal, runtime.Options,
            mutation => runtime.Apply(transaction, mutation));
        transaction.Commit();
        runtime.SetPosition(checkpoint.Position);
        return runtime.Journal.Position;
    }

    private static void RemoveCheckpointBaseline(ZoneTreeStoreRuntime runtime,
        ITransaction<Memory<byte>, Memory<byte>> transaction)
    {
        using var snapshot = new ZoneTreeSnapshotCursor(runtime.Tree);
        while (snapshot.Iterator.Next())
        {
            transaction.Delete(snapshot.Iterator.CurrentKey);
        }
    }

    private static bool ReplayFrame(ZoneTreeStoreRuntime runtime, byte[] header, ref long validLength)
    {
        var journal = runtime.Journal;
        var frame = (ZoneTreeJournalFrameReader.ReadComplete(journal, header, runtime.Position,
            runtime.Options.MaxFrameBytes, runtime.Identity.FormatVersion)).Value;
        if (frame is null)
        {
            journal.SetLength(validLength);
            return false;
        }
        var (payload, sequence, _) = frame.Value;

        using var transaction = runtime.Tree.BeginTransaction();
        foreach (var mutation in ZoneTreeJournalCodec.Deserialize(payload))
        {
            runtime.Apply(transaction, mutation);
        }

        transaction.Commit();
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
        if (length <= 0 || length > maxFrameBytes || position == long.MaxValue
            || sequence != position + 1)
        {
            throw Errors.Fail(ErrorCode.Corruption, JournalSequenceInvalid);
        }

        return (length, sequence);
    }
}

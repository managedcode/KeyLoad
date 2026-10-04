using System.Buffers.Binary;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeFormatUpgradeCheckpoint
{
    internal static long ReadSource(FileStream journal, ZoneTreeStoreOptions options, Guid expectedIncarnation,
        int sourceDataEpoch)
        => ReadSource(journal, options, static _ => { }, expectedIncarnation, sourceDataEpoch);

    internal static long ReadSource(FileStream journal, ZoneTreeStoreOptions options,
        Action<StorageMutation> apply, Guid expectedIncarnation, int sourceDataEpoch)
    {
        if (journal.Length == 0)
        { return 0; }
        if (journal.Length < sizeof(ulong))
        { throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.JournalSequenceInvalid); }
        Span<byte> magicBytes = stackalloc byte[sizeof(ulong)];
        journal.ReadExactly(magicBytes);
        journal.Position = 0;
        var magic = BinaryPrimitives.ReadUInt64LittleEndian(magicBytes);
        if (sourceDataEpoch == ZoneTreePersistenceFormat.Native5DataEpoch
            && magic == ZoneTreePersistenceFormat.SourceCheckpointMagic)
        {
            return ZoneTreeCheckpointReader.ReadNative3ForUpgrade(journal, options, apply,
                expectedIncarnation).Position;
        }
        if (sourceDataEpoch == ZoneTreePersistenceFormat.Native6DataEpoch
            && magic == ZoneTreePersistenceFormat.Native6CheckpointMagic)
        {
            return ZoneTreeCheckpointReader.ReadNative4ForUpgrade(journal, options, apply,
                expectedIncarnation).Position;
        }
        if (magic == ZoneTreePersistenceFormat.JournalMagic)
        { return 0; }
        throw Errors.Fail(ErrorCode.FormatUnsupported, ZoneTreePersistenceFormat.JournalFormatUpgradeRequired);
    }

    internal static long ReadCurrent(FileStream journal, ZoneTreeStoreOptions options, Guid expectedIncarnation)
    {
        if (journal.Length == 0)
        { return 0; }
        if (journal.Length < sizeof(ulong))
        { throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.JournalSequenceInvalid); }
        Span<byte> magicBytes = stackalloc byte[sizeof(ulong)];
        journal.ReadExactly(magicBytes);
        journal.Position = 0;
        var magic = BinaryPrimitives.ReadUInt64LittleEndian(magicBytes);
        if (magic == ZoneTreePersistenceFormat.CheckpointMagic)
        {
            var checkpoint = ZoneTreeCheckpointReader.Read(journal, options, static _ => { });
            if (checkpoint.Incarnation != expectedIncarnation)
            { throw Errors.Fail(ErrorCode.TokenInvalidated, ZoneTreePersistenceFormat.SnapshotScopeInvalid); }
            return checkpoint.Position;
        }
        if (magic == ZoneTreePersistenceFormat.JournalMagic)
        { return 0; }
        throw Errors.Fail(ErrorCode.FormatUnsupported, ZoneTreePersistenceFormat.JournalFormatUpgradeRequired);
    }
}

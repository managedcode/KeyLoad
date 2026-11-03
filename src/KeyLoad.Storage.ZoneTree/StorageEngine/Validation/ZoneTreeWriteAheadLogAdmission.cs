using ZoneTree;
using ZoneTree.Core;
using ZoneTree.Options;
using ZoneTree.WAL;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Checks stored metadata before open and the native writer before admitting operations.</summary>
internal static class ZoneTreeWriteAheadLogAdmission
{
    private const string RequiredMode = "The opened store does not provide the required process-persistent WAL publication.";

    internal static void RequireStoredProcessPersistence<TKey, TValue>(ZoneTreeFactory<TKey, TValue> factory)
    {
        var options = factory.Options;
        if (!ZoneTreeMetaWAL<TKey, TValue>.Exists(options))
        {
            return;
        }
        var stored = ZoneTreeMetaWAL<TKey, TValue>.LoadZoneTreeMetaWithoutWALRecords(options.RandomAccessDeviceManager);
        if (stored.WriteAheadLogOptions.WriteAheadLogMode is not (WriteAheadLogMode.SyncCompressed or WriteAheadLogMode.Shared))
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, RequiredMode);
        }
    }

    internal static void RequireProcessPersistence<TKey, TValue>(IZoneTree<TKey, TValue> tree)
    {
        var maintenance = tree.Maintenance;
        var provider = maintenance.CloneOptions().WriteAheadLogProvider;
        var wal = provider.GetWAL<TKey, TValue>(maintenance.MutableSegment.SegmentId,
            global::ZoneTree.Core.ZoneTree<TKey, TValue>.SegmentWalCategory);
        if (wal is not (SyncCompressedFileSystemWriteAheadLog<TKey, TValue> or SharedWriteAheadLog<TKey, TValue>))
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, RequiredMode);
        }
    }
}

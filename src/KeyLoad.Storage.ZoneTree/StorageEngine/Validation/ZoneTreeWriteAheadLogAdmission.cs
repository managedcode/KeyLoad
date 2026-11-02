using ZoneTree;
using ZoneTree.WAL;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Checks the opened native writer rather than the configuration requested for future WAL files.</summary>
internal static class ZoneTreeWriteAheadLogAdmission
{
    private const string RequiredMode = "The opened store does not provide the required process-persistent WAL publication.";

    internal static void RequireProcessPersistence<TKey, TValue>(IZoneTree<TKey, TValue> tree)
    {
        var maintenance = tree.Maintenance;
        var provider = maintenance.CloneOptions().WriteAheadLogProvider;
        var wal = provider.GetWAL<TKey, TValue>(maintenance.MutableSegment.SegmentId,
            global::ZoneTree.Core.ZoneTree<TKey, TValue>.SegmentWalCategory);
        if (wal is not SyncCompressedFileSystemWriteAheadLog<TKey, TValue>)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, RequiredMode);
        }
    }
}

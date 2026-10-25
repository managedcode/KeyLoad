using KeyLoad.Core.BackupRestore.Serialization;
using KeyLoad.Core.StoragePartitions.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.RelationalStorage;

/// <summary>Collects the actual table-owned records before one bounded atomic table removal.</summary>
internal static class TableDataCleanup
{
    private const string Invalid = "The persisted atomic partition inventory is invalid.";
    private static readonly string[] Families =
    [
        PartitionRecordFamilies.Document, PartitionRecordFamilies.Index, PartitionRecordFamilies.Unique,
        PartitionRecordFamilies.DocumentEpoch, PartitionRecordFamilies.VisibilityEpoch,
        PartitionRecordFamilies.Vector, PartitionRecordFamilies.VectorProjectionLineage,
        PartitionRecordFamilies.Bm25CorpusCurrent, PartitionRecordFamilies.OnlineTextCurrent
    ];

    internal static List<byte[]> Capture(IKeyValueView view, ResourceLifetime lifetime,
        ReadExecutionBudget work)
    {
        var partitions = new List<PartitionRef>();
        _ = work.VisitAll(view, AtomicPartitionRosterKeys.Prefix(), (key, value) =>
            {
                var entry = OrleansSerialization.Deserialize<AtomicPartitionCatalogEntry>(value);
                EngineResourceAccess.ValidatePartition(entry.Partition);
                if (entry.Version != AtomicPartitionRosterProtocol.CurrentVersion
                    || !key.SequenceEqual(AtomicPartitionRosterKeys.Partition(entry.Partition)))
                {
                    throw Errors.Fail(ErrorCode.Corruption, Invalid);
                }
                if (entry.Partition.TenantId == lifetime.TenantId && entry.Partition.DatabaseId == lifetime.DatabaseId
                    && entry.Partition.TransactionDomainId == lifetime.TransactionDomainId)
                {
                    partitions.Add(entry.Partition);
                }
                return true;
            });
        return CaptureKeys(view, lifetime, partitions, work);
    }

    private static List<byte[]> CaptureKeys(IKeyValueView view, ResourceLifetime lifetime,
        List<PartitionRef> partitions, ReadExecutionBudget work)
    {
        var keys = new List<byte[]>();
        foreach (var partition in partitions)
        {
            foreach (var family in Families)
            {
                var prefix = KeySpace.Partition(family, partition, lifetime.ResourceName);
                _ = work.VisitAll(view, prefix, (key, _) =>
                {
                    keys.Add(key.ToArray());
                    return true;
                });
            }
        }
        return keys;
    }
}

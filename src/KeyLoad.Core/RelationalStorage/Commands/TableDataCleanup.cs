using KeyLoad.Core.Features.BackupRestore.Serialization;
using KeyLoad.Core.Features.StoragePartitions.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.RelationalStorage;

/// <summary>Collects the actual table-owned records before one bounded atomic table removal.</summary>
internal static class TableDataCleanup
{
    private const long Empty = 0;
    private const string Capacity = "Table removal exceeds its configured atomic operation budget.";
    private const string Invalid = "The persisted atomic partition inventory is invalid.";
    private static readonly string[] Families =
    [
        PartitionRecordFamilies.Document, PartitionRecordFamilies.Index, PartitionRecordFamilies.Unique,
        PartitionRecordFamilies.DocumentEpoch, PartitionRecordFamilies.VisibilityEpoch,
        PartitionRecordFamilies.Vector, PartitionRecordFamilies.VectorProjectionLineage,
        PartitionRecordFamilies.Bm25CorpusCurrent, PartitionRecordFamilies.OnlineTextCurrent
    ];

    internal static List<byte[]> Capture(IKeyValueView view, ResourceLifetime lifetime,
        ReadExecutionBudget work, DatabaseLimits limits)
    {
        var partitions = new List<PartitionRef>();
        var inventory = work.VisitRange(view, AtomicPartitionRosterKeys.Prefix(),
            limits.MaxScanRecords, (key, value) =>
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
        if (inventory.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Capacity);
        }
        return CaptureKeys(view, lifetime, partitions, work, limits);
    }

    private static List<byte[]> CaptureKeys(IKeyValueView view, ResourceLifetime lifetime,
        List<PartitionRef> partitions, ReadExecutionBudget work, DatabaseLimits limits)
    {
        var keys = new List<byte[]>();
        var retainedBytes = Empty;
        foreach (var partition in partitions)
        {
            foreach (var family in Families)
            {
                var prefix = KeySpace.Partition(family, partition, lifetime.ResourceName);
                var scan = work.VisitRange(view, prefix, limits.MaxScanRecords, (key, _) =>
                {
                    if (key.Length > limits.MaxBatchBytes - retainedBytes || keys.Count >= limits.MaxScanRecords)
                    {
                        throw Errors.Fail(ErrorCode.BudgetExceeded, Capacity);
                    }
                    keys.Add(key.ToArray());
                    retainedBytes += key.Length;
                    return true;
                });
                if (scan.HasMore)
                {
                    throw Errors.Fail(ErrorCode.BudgetExceeded, Capacity);
                }
            }
        }
        return keys;
    }
}

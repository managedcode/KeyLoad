using System.Security.Cryptography;
using KeyLoad.Core.Features.BackupRestore.Serialization;
using KeyLoad.Core.Features.BackupRestore.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

/// <summary>Validates every exact immutable native roster occurrence before a capture can be admitted.</summary>
internal static class ClusterBackupRosterCapture
{
    private const string InvalidRoster = "The current cluster capture roster is missing or corrupt.";
    internal sealed record CapturedEntry(AtomicPartitionCatalogEntryV1 Entry, string Digest);

    internal static Dictionary<PartitionRef, CapturedEntry> Read(IKeyValueView view, Guid incarnation, long position,
        long applied, DatabaseLimits limits)
    {
        var result = new Dictionary<PartitionRef, CapturedEntry>();
        var range = view.VisitRange(AtomicPartitionRosterRestoreOriginSerialization.EntryPrefix(), limits.MaxScanRecords,
            (key, value) =>
            {
                if (value.Length > limits.MaxBatchBytes)
                { throw Errors.Fail(ErrorCode.Corruption, InvalidRoster); }
                var entry = NativeSerialization.Deserialize<AtomicPartitionCatalogEntryV1>(value);
                if (entry is null || entry.Partition is null
                    || !key.SequenceEqual(AtomicPartitionRosterKeys.Partition(entry.Partition)))
                { throw Errors.Fail(ErrorCode.Corruption, InvalidRoster); }
                var bound = AtomicPartitionRosterOriginValidation.ReadBound(view, entry, value.ToArray(), incarnation);
                AtomicPartitionRosterEntryValidation.Validate(entry, entry.Partition, position, Math.Max(applied, bound));
                if (!result.TryAdd(entry.Partition, new(entry, Convert.ToHexString(SHA256.HashData(value)))))
                { throw Errors.Fail(ErrorCode.Corruption, InvalidRoster); }
                return true;
            });
        if (range.HasMore || range.StoppedByVisitor)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidRoster); }
        return result;
    }
}

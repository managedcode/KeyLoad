using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

/// <summary>Preserves native decision order across independent data and metadata command owners.</summary>
public static class StorageReplicaDependencies
{
    /// <summary>A copy must await these observed prefixes before publishing any of the dependent effects.</summary>
    public const string NotReady = "The storage copy has not persisted the decision's prerequisite command prefixes.";

    internal static ImmutableArray<StoragePartitionPosition> Capture(IKeyValueView view, PartitionRef? partition,
        IEnumerable<PartitionRef>? readPartitions)
    {
        if (partition is null)
        {
            return [.. StorageReplicaExecution.ReadPartitionPositions(view)
                .Where(cut => cut.Partition is not null && cut.Position > 0)];
        }
        var dependencies = ImmutableArray.CreateBuilder<StoragePartitionPosition>();
        var metadata = StorageOperationApply.ReadCanonicalSequence(view, null);
        if (metadata > 0)
        {
            dependencies.Add(new(null, metadata));
        }
        foreach (var observed in readPartitions ?? [])
        {
            var position = StorageOperationApply.ReadCanonicalSequence(view, observed);
            if (position > 0)
            {
                dependencies.Add(new(observed, position));
            }
        }
        return dependencies.ToImmutable();
    }

    internal static void Require(IKeyValueView view, StorageReplicaBatch batch)
    {
        if (batch.Dependencies.IsDefault || batch.Dependencies.Length > view.MaximumRangeRecords)
        {
            throw Errors.Fail(ErrorCode.Corruption, NotReady);
        }
        foreach (var cut in batch.Dependencies)
        {
            if (cut.Position <= 0 || cut.Partition == batch.Partition)
            {
                throw Errors.Fail(ErrorCode.Corruption, NotReady);
            }
            if (StorageOperationApply.ReadCanonicalSequence(view, cut.Partition) < cut.Position)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, NotReady);
            }
        }
    }

    /// <summary>Reads only missing prerequisite prefixes after a dependent publication was deferred.</summary>
    public static ImmutableArray<StoragePartitionPosition> Missing(DatabaseEngine database, StorageReplicaBatch batch,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(database) /* KLD0041: The BCL guard validates the original DatabaseEngine reference. */;
        ArgumentNullException.ThrowIfNull(batch) /* KLD0041: The BCL guard validates the generated StorageReplicaBatch reference. */;
        return database.Store.Read(view =>
        {
            var missing = ImmutableArray.CreateBuilder<StoragePartitionPosition>();
            foreach (var cut in batch.Dependencies)
            {
                token.ThrowIfCancellationRequested();
                if (StorageOperationApply.ReadCanonicalSequence(view, cut.Partition) < cut.Position)
                {
                    missing.Add(cut);
                }
            }
            return missing.ToImmutable();
        });
    }
}

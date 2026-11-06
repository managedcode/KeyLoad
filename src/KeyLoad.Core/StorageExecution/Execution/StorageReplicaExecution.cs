using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

/// <summary>Copies committed effects without repeating business decisions against a different authorization cut.</summary>
public static class StorageReplicaExecution
{
    private const string CutVectorExceeded = "The partition cut vector exceeds the storage read bound.";
    private const string InvalidBatch = "The storage copy does not continue this partition's retained decision.";

    internal static StorageBatchResult RetainReplicaBatch(this DatabaseEngine engine, IAtomicTransaction transaction,
        PartitionRef? partition, long previous, Guid batchId, StorageBatchResult result,
        IEnumerable<PartitionRef>? readPartitions)
    {
        var staging = transaction as IAtomicActionStaging
            ?? throw new InvalidOperationException(StorageOperationBatchApply.ActionStagingRequired);
        var batch = new StorageReplicaBatch(engine.Store.Identity.Incarnation, batchId, partition,
            previous, result.Position, [.. staging.CaptureWrites()], [.. result.Applied.Select(StorageCopyBatchAcknowledgements.Decision)])
        {
            Dependencies = StorageReplicaDependencies.Capture(transaction, partition, readPartitions)
        };
        transaction.PutRecord(StorageCommandKeys.ReplicaBatch(partition), batch);
        return result with { Replication = batch };
    }

    /// <summary>Reads the last atomically retained effects of one command owner for an interrupted copy.</summary>
    public static StorageReplicaBatch? ReadReplicaBatch(this DatabaseEngine engine, PartitionRef? partition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine) /* KLD0041: The BCL guard validates the existing DatabaseEngine reference without copying it. */;
        return engine.Store.Read(view =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return view.GetRecord<StorageReplicaBatch>(StorageCommandKeys.ReplicaBatch(partition));
        });
    }

    /// <summary>Publishes the exact primary effects and their retained decision in one native commit.</summary>
    public static Task<StorageCopyBatchAcknowledgement> ApplyReplicaBatchAsync(this DatabaseEngine engine, StorageReplicaBatch batch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine) /* KLD0041: The BCL guard validates the existing DatabaseEngine reference without copying it. */;
        ArgumentNullException.ThrowIfNull(batch) /* KLD0041: The BCL guard validates the exact generated StorageReplicaBatch reference. */;
        return engine.Store.CommitAsync((transaction, _) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (batch.Incarnation != engine.Store.Identity.Incarnation || batch.BatchId == Guid.Empty)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidBatch);
            }
            var current = StorageOperationApply.ReadCanonicalSequence(transaction, batch.Partition);
            if (current == batch.Position && transaction.GetRecord<StorageReplicaBatch>(
                    StorageCommandKeys.ReplicaBatch(batch.Partition))?.BatchId == batch.BatchId)
            {
                return new StorageCopyBatchAcknowledgement(current, batch.Applied, null);
            }
            if (batch.PreviousPosition != current || batch.Position <= current || batch.Changes.IsDefaultOrEmpty)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidBatch);
            }
            StorageReplicaDependencies.Require(transaction, batch);
            foreach (var change in batch.Changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (change.Value is { } value)
                {
                    transaction.Put(change.Key.ToArray(), value.ToArray());
                }
                else
                {
                    transaction.Delete(change.Key.ToArray());
                }
            }
            if (StorageOperationApply.ReadCanonicalSequence(transaction, batch.Partition) != batch.Position)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidBatch);
            }
            transaction.PutRecord(StorageCommandKeys.ReplicaBatch(batch.Partition), batch);
            return new StorageCopyBatchAcknowledgement(batch.Position, batch.Applied, null);
        }, cancellationToken);
    }

    /// <summary>Captures a bounded vector of partition cuts from one native snapshot.</summary>
    public static ImmutableArray<StoragePartitionPosition> ReadPartitionPositions(IKeyValueView view,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(view) /* KLD0041: The BCL guard validates the existing IKeyValueView reference. */;
        cancellationToken.ThrowIfCancellationRequested();
        var page = view.Scan(StorageCommandKeys.AppliedPrefix, view.MaximumRangeRecords);
        if (page.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, CutVectorExceeded);
        }
        var positions = ImmutableArray.CreateBuilder<StoragePartitionPosition>(page.Records.Length);
        foreach (var record in page.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var position = OrleansSerialization.Deserialize<StoragePartitionPosition>(record.Value.Span);
            if (position.Position < 0 || !StorageCommandKeys.Applied(position.Partition).AsSpan().SequenceEqual(record.Key.Span))
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidBatch);
            }
            positions.Add(position);
        }
        return positions.MoveToImmutable();
    }
}

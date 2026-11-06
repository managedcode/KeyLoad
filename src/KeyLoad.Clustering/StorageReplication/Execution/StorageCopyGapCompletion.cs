using Microsoft.Extensions.Logging;

namespace KeyLoad.Clustering;

/// <summary>Completes the last persisted canonical batch on lagging copies through typed Orleans service calls.</summary>
internal sealed class StorageCopyGapCompletion(SiloDatabaseClient storage, ILogger diagnostics, PartitionRef? partition)
{
    /// <summary>Completes every owner that missed part of the last committed batch and returns the shared cut.</summary>
    internal async Task<long> CompleteAsync(StorageCopyOwner[] owners, CancellationToken cancellationToken)
    {
        var maximum = owners[StorageCopyContract.FirstOwner].Cut.Position;
        if (maximum == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, StorageCopyContract.SequenceExhausted);
        }
        if (owners[^StorageCopyContract.LastOwnerFromEnd].Cut.Position == maximum)
        {
            return maximum;
        }

        var committed = await storage.On(owners[StorageCopyContract.FirstOwner].Address)
            .ReadReplicaBatchAsync(partition, cancellationToken);
        var completed = StorageCopyContract.UnpersistedCopies;
        foreach (var owner in owners)
        {
            if (owner.Cut.Position == maximum)
            {
                continue;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (committed is null || committed.PreviousPosition != owner.Cut.Position || committed.Position != maximum)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, StorageCopyContract.RecoveryRequired);
            }
            var applied = await storage.CopyAsync(owner.Address, committed, cancellationToken);
            if (applied.Position != maximum)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, StorageCopyContract.RecoveryRequired);
            }
            completed++;
        }
        StorageCopyDiagnostics.CopyGapCompleted(diagnostics, maximum, completed);
        return maximum;
    }

}

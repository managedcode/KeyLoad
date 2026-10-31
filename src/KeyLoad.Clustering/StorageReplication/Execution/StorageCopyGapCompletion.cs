using System.Collections.Immutable;
using ManagedCode.Communication.Commands;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Clustering;

/// <summary>Completes the last persisted canonical batch on lagging copies through typed Orleans service calls.</summary>
internal sealed class StorageCopyGapCompletion(SiloDatabaseClient storage, ILogger diagnostics)
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
            .ReadLastCommittedCommandsAsync(cancellationToken).ConfigureAwait(true);
        var completed = StorageCopyContract.UnpersistedCopies;
        foreach (var owner in owners)
        {
            if (owner.Cut.Position == maximum)
            {
                continue;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var missing = Missing(committed, owner.Cut.Position, maximum);
            var applied = await storage.On(owner.Address).ApplyBatchAsync(missing,
                owner.Cut.Position + StorageCopyContract.NextSequence, cancellationToken).ConfigureAwait(true);
            if (applied.Position != maximum || applied.Applied.Length != missing.Length)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, StorageCopyContract.RecoveryRequired);
            }
            completed++;
        }
        StorageCopyDiagnostics.CopyGapCompleted(diagnostics, maximum, completed);
        return maximum;
    }

    /// <summary>Selects the committed commands after <paramref name="position"/>; they must reach the shared cut.</summary>
    private static ImmutableArray<Command<DatabaseCommand>> Missing(ImmutableArray<StoredCommand> committed,
        long position, long maximum)
    {
        var missing = committed.Where(command => command.Sequence > position).ToImmutableArray();
        if (missing.Length == 0 || missing.Length != maximum - position
            || missing[StorageCopyContract.FirstOwner].Sequence != position + StorageCopyContract.NextSequence)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, StorageCopyContract.RecoveryRequired);
        }
        return [.. missing.Select(command => command.Command)];
    }
}

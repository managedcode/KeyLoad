using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Copies prepared commands through execution storage services under the owning grain's ordering.</summary>
internal sealed class SiloStorageCopyExecution(SiloDatabaseClient storage, IClusterMembershipService membership,
    IOptions<ReplicaCopyOptions> options, Guid incarnation, ILogger diagnostics)
{
    private readonly record struct AvailableCopy(SiloAddress Address, string Name, SiloStorageCut Cut);

    /// <summary>Applies one command to every selected copy; any failure after the first persisted copy is uncertain.</summary>
    internal async Task<Result<object>> ApplyAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owners = await OwnersAsync(cancellationToken).ConfigureAwait(true);
        var prepared = await PrepareAsync(owners, operation, cancellationToken).ConfigureAwait(true);
        var persisted = StorageCopyContract.UnpersistedCopies;
        try
        {
            StorageOperationResult? original = null;
            foreach (var owner in owners)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var applied = await storage.On(owner.Address).ApplyAsync(prepared.Command, prepared.Sequence, cancellationToken).ConfigureAwait(true);
                persisted++;
                original ??= applied;
                if (applied.Sequence != original.Sequence || !StorageCopyAcknowledgements.Same(applied.Result, original.Result))
                {
                    throw Errors.Fail(ErrorCode.UnknownWriteOutcome, StorageCopyContract.DifferentOutcomes);
                }
            }
            return StorageCopyAcknowledgements.Attach(original!.Result, owners.Length);
        }
        catch (Exception error) when (error is not KeyLoadException { Code: ErrorCode.UnknownWriteOutcome }
            && (persisted > StorageCopyContract.UnpersistedCopies
                || error is OperationCanceledException or OrleansException or TimeoutException))
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, StorageCopyContract.WriteUncertain, error);
        }
    }

    /// <summary>Reads the most advanced available cut of this incarnation.</summary>
    internal async Task<SiloStorageCut> ReadCutAsync(CancellationToken cancellationToken)
        => (await ReadAvailableAsync(cancellationToken).ConfigureAwait(true)).Cut;

    /// <summary>Selects the most advanced available copy, preferring the caller's silo on ties.</summary>
    internal async Task<SiloAddress> GetReadOwnerAsync(SiloAddress preferredSilo, CancellationToken cancellationToken)
        => (await ReadAvailableAsync(cancellationToken, preferredSilo).ConfigureAwait(true)).Address;

    /// <summary>Owners are the copies holding the most data, so a joining or lagging silo never displaces a complete copy.</summary>
    private async Task<AvailableCopy[]> OwnersAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        settings.Validate();
        var available = await ReadAvailableCopiesAsync(cancellationToken).ConfigureAwait(true);
        var owners = available.OrderByDescending(copy => copy.Cut.Position)
            .ThenBy(copy => copy.Name, StringComparer.Ordinal).Take(settings.ReplicaCount).ToArray();
        if (owners.Length != settings.ReplicaCount)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, StorageCopyContract.OwnersUnavailable);
        }
        return owners;
    }

    private async Task<AvailableCopy[]> ReadAvailableCopiesAsync(CancellationToken cancellationToken)
    {
        var members = membership.CurrentSnapshot.Members.Values.Where(member => member.Status == SiloStatus.Active)
            .OrderBy(member => member.Name, StringComparer.Ordinal).ToArray();
        var reads = new Task<SiloStorageCut?>[members.Length];
        for (var index = StorageCopyContract.FirstOwner; index < members.Length; index++)
        {
            reads[index] = TryReadCutAsync(members[index].SiloAddress, cancellationToken);
        }
        var cuts = await Task.WhenAll(reads).ConfigureAwait(true);
        var available = new List<AvailableCopy>(members.Length);
        for (var index = StorageCopyContract.FirstOwner; index < members.Length; index++)
        {
            if (cuts[index] is { } cut)
            {
                available.Add(new(members[index].SiloAddress, members[index].Name, cut));
            }
        }
        return [.. available];
    }

    /// <summary>A silo whose store is closed, mismatched or unreachable is excluded instead of failing every operation.</summary>
    private async Task<SiloStorageCut?> TryReadCutAsync(SiloAddress silo, CancellationToken cancellationToken)
    {
        try
        {
            var cut = await storage.On(silo).ReadCutAsync(cancellationToken).ConfigureAwait(true);
            if (cut.Incarnation == incarnation)
            {
                return cut;
            }
            StorageCopyDiagnostics.CopyExcluded(diagnostics, silo, ErrorCode.RecoveryRequired);
            return null;
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested && CqrsBoundaryErrors.IsNonFatal(error))
        {
            StorageCopyDiagnostics.CopyExcluded(diagnostics, silo,
                error is KeyLoadException failure ? failure.Code : ErrorCode.OwnershipLost);
            return null;
        }
    }

    private async Task<AvailableCopy> ReadAvailableAsync(CancellationToken cancellationToken, SiloAddress? preferredSilo = null)
    {
        AvailableCopy? selected = null;
        foreach (var copy in await ReadAvailableCopiesAsync(cancellationToken).ConfigureAwait(true))
        {
            if (selected is not { } current || copy.Cut.Position > current.Cut.Position
                || copy.Cut.Position == current.Cut.Position && copy.Address.Equals(preferredSilo))
            {
                selected = copy;
            }
        }
        return selected ?? throw Errors.Fail(ErrorCode.OwnershipLost, StorageCopyContract.OwnersUnavailable);
    }

    private async Task<StoredCommand> PrepareAsync(AvailableCopy[] owners, Command<DatabaseCommand> operation,
        CancellationToken cancellationToken)
    {
        var newest = owners[StorageCopyContract.FirstOwner];
        var maximum = newest.Cut.Position;
        if (owners[^StorageCopyContract.LastOwnerFromEnd].Cut.Position != maximum)
        {
            await CompleteCopyGapAsync(owners, maximum, cancellationToken).ConfigureAwait(true);
        }
        var stored = await storage.On(newest.Address).ReadStoredCommandAsync(operation, cancellationToken).ConfigureAwait(true);
        if (stored is not null)
        {
            return stored;
        }
        if (maximum == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, StorageCopyContract.SequenceExhausted);
        }
        var prepared = await storage.On(newest.Address).PrepareAsync(operation, cancellationToken).ConfigureAwait(true);
        return new(maximum + StorageCopyContract.NextSequence, prepared);
    }

    /// <summary>Completes the last canonical command on copies one position behind, whichever client issued it.</summary>
    private async Task CompleteCopyGapAsync(AvailableCopy[] owners, long maximum, CancellationToken cancellationToken)
    {
        if (maximum - owners[^StorageCopyContract.LastOwnerFromEnd].Cut.Position != StorageCopyContract.NextSequence)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, StorageCopyContract.RecoveryRequired);
        }
        var incomplete = await storage.On(owners[StorageCopyContract.FirstOwner].Address)
            .ReadLastCommittedCommandAsync(cancellationToken).ConfigureAwait(true);
        if (incomplete is null || incomplete.Sequence != maximum)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, StorageCopyContract.RecoveryRequired);
        }
        var completed = StorageCopyContract.UnpersistedCopies;
        foreach (var owner in owners)
        {
            if (owner.Cut.Position == maximum)
            {
                continue;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var applied = await storage.On(owner.Address).ApplyAsync(incomplete.Command, incomplete.Sequence, cancellationToken).ConfigureAwait(true);
            if (applied.Sequence != maximum)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, StorageCopyContract.RecoveryRequired);
            }
            completed++;
        }
        StorageCopyDiagnostics.CopyGapCompleted(diagnostics, maximum, completed);
    }
}

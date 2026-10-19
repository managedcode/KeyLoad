using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Copies prepared commands through execution storage services under the owning grain's ordering.</summary>
internal sealed class SiloStorageCopyExecution(SiloDatabaseClient storage, IClusterMembershipService membership,
    IOptions<ReplicaCopyOptions> options, Guid incarnation)
{
    /// <inheritdoc />
    internal async Task<Result<object>> ApplyAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owners = Owners();
        var prepared = await PrepareAsync(owners, operation, cancellationToken).ConfigureAwait(true);
        try
        {
            StorageOperationResult? original = null;
            foreach (var owner in owners)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var applied = await storage.On(owner).ApplyAsync(prepared.Command, prepared.Sequence, cancellationToken).ConfigureAwait(true);
                original ??= applied;
                if (applied.Sequence != original.Sequence || !StorageCopyAcknowledgements.Same(applied.Result, original.Result))
                {
                    throw Errors.Fail(ErrorCode.UnknownWriteOutcome, StorageCopyContract.DifferentOutcomes);
                }
            }
            return StorageCopyAcknowledgements.Attach(original!.Result, owners.Length);
        }
        catch (Exception error) when (error is OperationCanceledException or OrleansException or TimeoutException)
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, StorageCopyContract.WriteUncertain, error);
        }
    }

    /// <inheritdoc />
    internal async Task<SiloStorageCut> ReadCutAsync(CancellationToken cancellationToken)
        => (await ReadAvailableAsync(cancellationToken).ConfigureAwait(true)).Cut;

    /// <inheritdoc />
    internal async Task<SiloAddress> GetReadOwnerAsync(SiloAddress preferredSilo, CancellationToken cancellationToken)
        => (await ReadAvailableAsync(cancellationToken, preferredSilo).ConfigureAwait(true)).Owner;

    private SiloAddress[] Owners()
    {
        var settings = options.Value;
        settings.Validate();
        var owners = ActiveSilos().Take(settings.ReplicaCount).ToArray();
        if (owners.Length != settings.ReplicaCount)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, StorageCopyContract.OwnersUnavailable);
        }
        return owners;
    }

    private SiloAddress[] ActiveSilos()
        => membership.CurrentSnapshot.Members.Values.Where(member => member.Status == SiloStatus.Active)
            .OrderBy(member => member.Name, StringComparer.Ordinal).Select(member => member.SiloAddress).ToArray();

    private async Task<StoredCommand> PrepareAsync(SiloAddress[] owners, Command<DatabaseCommand> operation,
        CancellationToken cancellationToken)
    {
        var cuts = new SiloStorageCut[owners.Length];
        for (var index = StorageCopyContract.FirstOwner; index < owners.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cuts[index] = await ReadCutAsync(owners[index], cancellationToken).ConfigureAwait(true);
        }
        var maximum = cuts.Max(cut => cut.Position);
        var minimum = cuts.Min(cut => cut.Position);
        var newest = owners[Array.FindIndex(cuts, cut => cut.Position == maximum)];
        var stored = await storage.On(newest).ReadStoredCommandAsync(operation, cancellationToken).ConfigureAwait(true);
        if (minimum != maximum && (stored is null || stored.Sequence != maximum ||
            maximum - minimum != StorageCopyContract.NextSequence))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, StorageCopyContract.RecoveryRequired);
        }
        if (stored is not null)
        {
            return stored;
        }
        if (maximum == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, StorageCopyContract.SequenceExhausted);
        }
        var prepared = await storage.On(newest).PrepareAsync(operation, cancellationToken).ConfigureAwait(true);
        return new(maximum + StorageCopyContract.NextSequence, prepared);
    }

    private async Task<(SiloAddress Owner, SiloStorageCut Cut)> ReadAvailableAsync(CancellationToken cancellationToken,
        SiloAddress? preferredSilo = null)
    {
        SiloAddress? selectedOwner = null;
        SiloStorageCut? selectedCut = null;
        foreach (var owner in ActiveSilos())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cut = await ReadCutAsync(owner, cancellationToken).ConfigureAwait(true);
            if (selectedCut is null || cut.Position > selectedCut.Position ||
                cut.Position == selectedCut.Position && owner.Equals(preferredSilo))
            {
                selectedOwner = owner;
                selectedCut = cut;
            }
        }
        return selectedCut is not null ? (selectedOwner!, selectedCut)
            : throw Errors.Fail(ErrorCode.OwnershipLost, StorageCopyContract.OwnersUnavailable);
    }

    private async Task<SiloStorageCut> ReadCutAsync(SiloAddress owner, CancellationToken cancellationToken)
    {
        var cut = await storage.On(owner).ReadCutAsync(cancellationToken).ConfigureAwait(true);
        if (cut.Incarnation != incarnation)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, StorageCopyContract.RecoveryRequired);
        }
        return cut;
    }
}

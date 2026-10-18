using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Orleans serializes commands; storage remains in the addressed silo-local Grain Services.</summary>
public sealed class DatabaseCommitGrain(SiloDatabaseClient storage, IClusterMembershipService membership,
    IOptions<ReplicaCopyOptions> options) : Grain, IDatabaseCommitGrain
{
    /// <inheritdoc />
    public async Task<OperationResult> ApplyAsync(ReplicatedOperation operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owners = Owners();
        await RequireMatchingCutsAsync(owners, cancellationToken).ConfigureAwait(true);
        var prepared = await storage.On(owners[StorageCopyContract.FirstOwner]).PrepareAsync(operation, cancellationToken).ConfigureAwait(true);
        try
        {
            OperationResult? result = null;
            foreach (var owner in owners)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var applied = await storage.On(owner).ApplyAsync(prepared, cancellationToken).ConfigureAwait(true);
                result ??= applied;
                if (applied.Error != result.Error || applied.Json != result.Json)
                {
                    throw Errors.Fail(ErrorCode.RecoveryRequired, StorageCopyContract.DifferentOutcomes);
                }
            }
            return result!;
        }
        catch (Exception error) when (error is OperationCanceledException or OrleansException or TimeoutException)
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, StorageCopyContract.WriteUncertain, error);
        }
    }

    /// <inheritdoc />
    public Task<SiloStorageCut> ReadCutAsync(CancellationToken cancellationToken)
        => RequireMatchingCutsAsync(Owners(), cancellationToken);

    /// <inheritdoc />
    public async Task<SiloAddress> GetReadOwnerAsync(CancellationToken cancellationToken)
    {
        var owners = Owners();
        await RequireMatchingCutsAsync(owners, cancellationToken).ConfigureAwait(true);
        return owners[StorageCopyContract.FirstOwner];
    }

    private SiloAddress[] Owners()
    {
        var settings = options.Value;
        settings.Validate();
        var owners = membership.CurrentSnapshot.Members.Values
            .Where(member => member.Status == SiloStatus.Active)
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .Take(settings.ReplicaCount).Select(member => member.SiloAddress).ToArray();
        if (owners.Length != settings.ReplicaCount)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, StorageCopyContract.OwnersUnavailable);
        }
        return owners;
    }

    private async Task<SiloStorageCut> RequireMatchingCutsAsync(SiloAddress[] owners, CancellationToken cancellationToken)
    {
        SiloStorageCut? expected = null;
        foreach (var owner in owners)
        {
            var actual = await storage.On(owner).ReadCutAsync(cancellationToken).ConfigureAwait(true);
            expected ??= actual;
            if (actual != expected || actual.Incarnation != this.GetPrimaryKey())
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, StorageCopyContract.RecoveryRequired);
            }
        }
        return expected!;
    }
}

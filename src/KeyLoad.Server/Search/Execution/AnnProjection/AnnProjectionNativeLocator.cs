using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.Search;

internal sealed record AnnProjectionNativeLocator(int VoterIndex, string SiloAddress,
    Guid NodeId, long ReadGeneration)
{
    internal static async Task<AnnProjectionNativeLocator> FindAsync(AnnProjectionNativeExchange exchanges,
        RegisteredPhysicalOwnerV1 owner, string principalId, AnnMaintenanceRequest original,
        DateTimeOffset expiry, global::KeyLoad.Core.ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        if (owner.Owner.VoterIds.IsDefaultOrEmpty)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, AnnProjectionObservationProtocol.Invalid);
        }
        AnnProjectionNativeLocator? found = null;
        for (var index = AnnProjectionNativeProtocol.FirstVoter; index < owner.Owner.VoterIds.Length; index++)
        {
            // The authentic local source cut already proves this voter is not the retained publication owner.
            // Do not enqueue a service child on the same connection while its public parent owns it.
            if (exchanges.RequireKnownLocalNonPublisher(owner, index, work))
            {
                continue;
            }
            var observed = await exchanges.ReadAsync(owner, index, principalId,
                new(AnnProjectionObservationProtocol.Version, AnnProjectionObservationKind.Identity, original,
                    work.RemainingReadGrantBytes, work.RemainingReadGrantRecords, null, null),
                expiry, work, cancellationToken).ConfigureAwait(false);
            if (observed.Result.Wait is not null || observed.Result.Approximate is not null)
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, AnnProjectionObservationProtocol.Invalid);
            }
            if (observed.Result.NodeId != original.NodeId)
            {
                continue;
            }
            if (found is not null)
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, AnnProjectionObservationProtocol.Invalid);
            }
            found = new(index, observed.SiloAddress, observed.Result.NodeId, observed.Result.ReadGeneration);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return found ?? throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionObservationProtocol.Invalid);
    }

    internal static void RequireResult(AnnProjectionNativeLocator locator, AnnMaintenanceRequest original,
        AnnProjectionNativeObservation observed)
    {
        var result = observed.Result;
        if (observed.SiloAddress != locator.SiloAddress || result.NodeId != locator.NodeId
            || result.NodeId != original.NodeId || result.ReadGeneration < locator.ReadGeneration)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionObservationProtocol.Invalid);
        }
    }
}

using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Reuses persisted credential and principal validation on the original owner-frame ledger.</summary>
internal sealed class ResourceRetirementBudgetedAdministrator(PartitionHost partition,
    PhysicalOwnerStartupRequests authentication, TimeProvider clock)
{
    internal async Task<PrincipalRecord> ReadAsync(string sourcePrincipalId, long sourcePolicyEpoch,
        bool technicalPrincipal, ReadExecutionBudget work, DateTimeOffset originalExpiry, CancellationToken token)
    {
        work.Check();
        token.ThrowIfCancellationRequested();
        var principal = technicalPrincipal
            ? await authentication.AuthenticateAsync(work, originalExpiry, token).ConfigureAwait(false)
            : partition.Database.Store.Read(raw => partition.Database.Principal(work.CreateView(raw),
                sourcePrincipalId, clock.GetUtcNow()));
        GrainRequestAuthority.RequireAdministrator(principal);
        if (!technicalPrincipal && principal.PolicyEpoch != sourcePolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementNativeProtocol.Unavailable);
        }
        work.Check();
        return principal;
    }
}

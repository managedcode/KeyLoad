using KeyLoad.Clustering.Search;
using KeyLoad.Core;

namespace KeyLoad.Server.Search;

internal sealed partial class TextOnlineMaintenanceService
{
    private Task<OnlineTextCapabilityResult?> ResolveCommittedOriginalAsync(PrincipalRecord principal,
        OnlineTextCapabilityRequest request, DateTimeOffset expiry, ReadExecutionBudget? originalBudget, CancellationToken token)
        => replay.ExecuteAsync(principal, request, expiry, token, originalBudget);
}

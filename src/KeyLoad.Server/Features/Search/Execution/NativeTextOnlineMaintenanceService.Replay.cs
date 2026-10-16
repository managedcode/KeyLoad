using KeyLoad.Clustering.Features.Search;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal sealed partial class NativeTextOnlineMaintenanceService
{
    private Task<OnlineTextCapabilityResult?> ResolveCommittedOriginalAsync(PrincipalRecord principal,
        OnlineTextCapabilityRequest request, DateTimeOffset expiry, CancellationToken token, ReadExecutionBudget? originalBudget)
        => replay.ExecuteAsync(principal, request, expiry, token, originalBudget);
}

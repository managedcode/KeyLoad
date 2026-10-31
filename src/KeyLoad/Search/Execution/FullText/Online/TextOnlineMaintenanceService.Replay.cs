using KeyLoad.Clustering.Search;
using KeyLoad.Core;

namespace KeyLoad.Server.Search;

internal sealed partial class TextOnlineMaintenanceService
{
    private async Task<OnlineTextCapabilityResult?> ResolveCommittedOriginalAsync(PrincipalRecord principal,
        OnlineTextCapabilityRequest request, ReadExecutionBudget? originalBudget, CancellationToken token)
        => (await replay.ExecuteAsync(principal, request, token, originalBudget)).Value;
}

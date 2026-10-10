using KeyLoad.Orleans.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal sealed partial class NativeTextOnlineMaintenanceService
{
    private Task<OnlineTextCapabilityResult?> ResolveCommittedOriginalAsync(PrincipalRecord principal,
        OnlineTextCapabilityRequest request, DateTimeOffset expiry, CancellationToken token)
        => replay.ExecuteAsync(principal, request, expiry, token);
}

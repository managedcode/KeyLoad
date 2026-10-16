using KeyLoad.Clustering.Features.Search;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

internal interface INativeOnlineTextMaintenance
{
    Task<OnlineTextCapabilityResult> ExecuteAsync(PrincipalRecord principal,
        OnlineTextCapabilityRequest request, DateTimeOffset originalExpiry, CancellationToken cancellationToken);
    OnlineTextPublicationWork RequirePublicationWork(Guid sessionId,
        OnlineTextIndexMaintenanceRequest request, string principalId);
    OnlineTextPublicationWork? TryRequireCheckpointWork(CommitProjectionBatchRequest request,
        string principalId, DateTimeOffset originalExpiry);
    Task<OnlineTextCapabilityResult> ExecuteBoundAsync(PrincipalRecord principal,
        OnlineTextCapabilityRequest request, DateTimeOffset originalExpiry,
        CancellationToken token, IResourceLifetimeWorkBinding binding);

    Task AbortAsync(Guid sessionId);
}

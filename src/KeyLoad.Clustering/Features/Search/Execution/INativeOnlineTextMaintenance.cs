using KeyLoad.Clustering.Features.Search;

namespace KeyLoad.Clustering;

internal interface INativeOnlineTextMaintenance
{
    Task<OnlineTextCapabilityResult> ExecuteAsync(PrincipalRecord principal,
        OnlineTextCapabilityRequest request, DateTimeOffset originalExpiry, CancellationToken cancellationToken);
    OnlineTextPublicationWork RequirePublicationWork(Guid sessionId,
        OnlineTextIndexMaintenanceRequest request, string principalId);
    OnlineTextPublicationWork? TryRequireCheckpointWork(CommitProjectionBatchRequest request,
        string principalId, DateTimeOffset originalExpiry);
    Task AbortAsync(Guid sessionId);
}

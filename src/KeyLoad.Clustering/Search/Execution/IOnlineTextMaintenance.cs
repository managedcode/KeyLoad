using KeyLoad.Clustering.Search;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Clustering;

internal interface IOnlineTextMaintenance
{
    Task<OnlineTextCapabilityResult> ExecuteAsync(PrincipalRecord principal,
        OnlineTextCapabilityRequest request, CancellationToken cancellationToken);
    OnlineTextPublicationWork RequirePublicationWork(Guid sessionId,
        OnlineTextIndexMaintenanceRequest request, string principalId);
    OnlineTextPublicationWork? TryRequireCheckpointWork(CommitProjectionBatchRequest request,
        string principalId);
    Task<OnlineTextCapabilityResult> ExecuteBoundAsync(PrincipalRecord principal,
        OnlineTextCapabilityRequest request,
        IResourceLifetimeWorkBinding binding, CancellationToken token);

    Task AbortAsync(Guid sessionId);
}

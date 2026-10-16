using KeyLoad.Core.Features.ResourceExecution;
namespace KeyLoad.Clustering;

/// <summary>Executes complete globally ranked canonical search in the existing connection-owned operation scope.</summary>
internal interface IRemoteDistributedSearchRouter
{
    /// <summary>Joins all exact-owner phases before returning a fully authorized page.</summary>
    /// <param name="envelope">Original verified call identity and absolute expiry.</param>
    /// <param name="principal">Actual freshly loaded source persisted principal.</param>
    /// <param name="request">The bounded caller search shape without private authority.</param>
    /// <param name="cancellationToken">Original independently owned call-local cancellation.</param>
    /// <param name="statisticsCaptured">Optional existing-profile observer of the fully settled original statistics phase.</param>
    /// <param name="binding">The explicit original native work binding and shared result-retention ledger.</param>
    /// <returns>A complete page bound to the original same-cut witnesses.</returns>
    Task<DistributedSearchPageV1> ReadAsync(GrainRequestEnvelope envelope, PrincipalRecord principal,
        DistributedSearchRequestV1 request, CancellationToken cancellationToken,
        Func<CancellationToken, Task>? statisticsCaptured, IResourceLifetimeWorkBinding binding);
}

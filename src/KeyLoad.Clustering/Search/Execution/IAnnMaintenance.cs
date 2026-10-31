using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Clustering;

// Invoked through the connection capability after a canonical read cut and current persisted authorization.
internal interface IAnnMaintenance
{
    Task<AnnProjectionEnrollment?> ReadEnrollmentAsync(PrincipalRecord principal,
        AnnMaintenanceRequest initialRequest, CancellationToken cancellationToken);

    Task<AnnProjectionPublication> ReadPublishedAsync(PrincipalRecord principal,
        AnnMaintenanceRequest request, CancellationToken cancellationToken);

    Task<AnnProjectionEnrollment?> ReadEnrollmentBoundAsync(PrincipalRecord principal,
        AnnMaintenanceRequest initialRequest, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding, CancellationToken token);

    Task<AnnProjectionPublication> ReadPublishedBoundAsync(PrincipalRecord principal,
        AnnMaintenanceRequest request, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding, CancellationToken token);

    Task<AnnMaintenanceResult?> AdmitOriginalAsync(PrincipalRecord principal, AnnMaintenanceRequest request,
        ProjectionConsumerInfo originalConfigure, CancellationToken cancellationToken);

    Task SettleOriginalResultAsync(PrincipalRecord principal, AnnMaintenanceRequest request,
        AnnMaintenanceResult originalResult, CancellationToken cancellationToken);

    Task AbortAsync(Guid sessionId);

    Task<AnnMaintenanceCapabilityResult> ExecuteBoundAsync(PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken);

    Task<AnnMaintenanceCapabilityResult> ExecuteAsync(PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, CancellationToken cancellationToken);
}

using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

// Invoked through the existing signed connection capability after quorum and persisted authority reload.
internal interface INativeAnnMaintenance
{
    Task<AnnProjectionEnrollment?> ReadEnrollmentAsync(PrincipalRecord principal,
        AnnMaintenanceRequest initialRequest, CancellationToken cancellationToken);

    Task<AnnProjectionPublication> ReadPublishedAsync(PrincipalRecord principal,
        AnnMaintenanceRequest request, CancellationToken cancellationToken);

    Task<AnnProjectionEnrollment?> ReadEnrollmentBoundAsync(PrincipalRecord principal,
        AnnMaintenanceRequest initialRequest, ReadExecutionBudget work, CancellationToken token, IResourceLifetimeWorkBinding binding);

    Task<AnnProjectionPublication> ReadPublishedBoundAsync(PrincipalRecord principal,
        AnnMaintenanceRequest request, ReadExecutionBudget work, CancellationToken token, IResourceLifetimeWorkBinding binding);

    Task<AnnMaintenanceResult?> AdmitOriginalAsync(PrincipalRecord principal, AnnMaintenanceRequest request,
        ProjectionConsumerInfo originalConfigure, CancellationToken cancellationToken);

    Task SettleOriginalResultAsync(PrincipalRecord principal, AnnMaintenanceRequest request,
        AnnMaintenanceResult originalResult, CancellationToken cancellationToken);

    Task AbortAsync(Guid sessionId);

    Task<AnnMaintenanceCapabilityResult> ExecuteBoundAsync(PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, DateTimeOffset requestExpiry, CancellationToken cancellationToken, IResourceLifetimeWorkBinding binding);

    Task<AnnMaintenanceCapabilityResult> ExecuteAsync(PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, CancellationToken cancellationToken);
}

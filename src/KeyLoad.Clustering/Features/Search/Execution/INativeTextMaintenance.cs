using KeyLoad.Core.Features.ResourceExecution;
namespace KeyLoad.Clustering;

/// <summary>Borrowed node-local text owner; every invocation is an independently signed request capability.</summary>
internal interface INativeTextMaintenance
{
    Task<TextMaintenanceCapabilityResult> ExecuteAsync(PrincipalRecord principal,
        TextMaintenanceCapabilityRequest request, CancellationToken cancellationToken);

    Task<TextMaintenanceCapabilityResult> ExecuteBoundAsync(PrincipalRecord principal,
        TextMaintenanceCapabilityRequest request, DateTimeOffset originalExpiry,
        CancellationToken token, IResourceLifetimeWorkBinding binding);

    Task AbortAsync(Guid sessionId);
}

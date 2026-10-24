using KeyLoad.Core.ResourceExecution;
namespace KeyLoad.Clustering;

/// <summary>Borrowed node-local text owner; every invocation is an independently issued request capability.</summary>
internal interface ITextMaintenance
{
    Task<TextMaintenanceCapabilityResult> ExecuteAsync(PrincipalRecord principal,
        TextMaintenanceCapabilityRequest request, CancellationToken cancellationToken);

    Task<TextMaintenanceCapabilityResult> ExecuteBoundAsync(PrincipalRecord principal,
        TextMaintenanceCapabilityRequest request, DateTimeOffset originalExpiry,
        IResourceLifetimeWorkBinding binding, CancellationToken token);

    Task AbortAsync(Guid sessionId);
}

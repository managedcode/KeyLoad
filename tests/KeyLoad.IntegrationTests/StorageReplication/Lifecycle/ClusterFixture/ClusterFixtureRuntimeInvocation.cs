namespace KeyLoad.IntegrationTests.Features.StorageReplication;

internal static class ClusterFixtureRuntimeInvocation
{
    internal const string RuntimeControllerUnavailable = "The Aspire execution process controller is not initialized.";
    internal const string DiagnosticsUnavailable = "The Aspire diagnostic capture is not initialized.";

    internal static Task KillAsync(Func<ProcessRuntimeControl> requireRuntime, string resourceName, string scenario, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario);
        return requireRuntime().KillAsync(resourceName, scenario, cancellationToken);
    }

    internal static Task RestartAsync(Func<ProcessRuntimeControl> requireRuntime, string resourceName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        return requireRuntime().RestartAsync(resourceName, cancellationToken);
    }

    internal static HttpAdmissionLimits RequireAdmission(HttpAdmissionLimits httpAdmission)
    {
        ArgumentNullException.ThrowIfNull(httpAdmission);
        return httpAdmission;
    }
}

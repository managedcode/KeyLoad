using KeyLoad.AppHost.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Composes the actual fixture-owned native restart acceptance phase separately from health.</summary>
internal static class ProcessRestartFixturePhases
{
    internal static Task<ProcessFaultCommandProtocol.LaunchIdentity> BeginProcessRestartAsync(this ClusterFixture fixture,
        string resourceName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        return fixture.RequireProcessRuntime().BeginRestartAsync(resourceName, cancellationToken);
    }
}

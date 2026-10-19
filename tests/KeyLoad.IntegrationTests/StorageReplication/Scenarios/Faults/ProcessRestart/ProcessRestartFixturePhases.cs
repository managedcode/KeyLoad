using KeyLoad.AppHost.Features.TestInfrastructure;

namespace KeyLoad.IntegrationTests.Features.StorageReplication;

/// <summary>Composes the actual fixture-owned execution restart acceptance phase separately from health.</summary>
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

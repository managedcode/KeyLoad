namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Composes the actual fixture-owned native restart acceptance phase separately from health.</summary>
internal static class ContainerRestartFixturePhases
{
    internal static Task<ContainerRuntimeInspection> BeginContainerRestartAsync(this ClusterFixture fixture,
        string resourceName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        return fixture.RequireContainerRuntime().BeginRestartAsync(resourceName, cancellationToken);
    }
}

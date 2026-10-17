namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Retains the original kill result pending a genuine Aspire restart.</summary>
/// <param name="Scenario">The qualification scenario.</param>
/// <param name="ResourceName">The actual Aspire resource name.</param>
/// <param name="ContainerName">The actual explicit model container name.</param>
/// <param name="Before">The running container inspected before kill.</param>
/// <param name="Stopped">The exited container inspected after kill.</param>
/// <param name="KillExitCode">The actual Docker kill exit code.</param>
/// <param name="KillOutput">The unchanged Docker kill output.</param>
/// <param name="KillError">The unchanged Docker kill standard error.</param>
internal sealed record ContainerRuntimeKillReceipt(string Scenario, string ResourceName, string ContainerName, ContainerRuntimeInspection Before,
    ContainerRuntimeInspection Stopped, int KillExitCode, string KillOutput, string KillError)
{
    public DateTimeOffset KillStartedAtUtc
    {
        get; init;
    }
    public DateTimeOffset KillCompletedAtUtc
    {
        get; init;
    }
}

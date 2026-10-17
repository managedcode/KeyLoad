using System.Text.Json.Serialization;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Preserves the original restart receipt property names, types and declaration order.</summary>
/// <param name="Scenario">The qualification scenario.</param>
/// <param name="ResourceName">The actual Aspire resource name.</param>
/// <param name="ContainerName">The actual explicit model container name.</param>
/// <param name="BeforeContainerId">The full container ID before kill.</param>
/// <param name="BeforeImage">The configured image before kill.</param>
/// <param name="BeforeImageId">The actual image ID before kill.</param>
/// <param name="BeforeState">The running state before kill.</param>
/// <param name="DockerKillExitCode">The actual Docker kill exit code.</param>
/// <param name="KillOutput">The unchanged Docker kill output.</param>
/// <param name="KillError">The unchanged Docker kill standard error.</param>
/// <param name="StoppedState">The verified exited state.</param>
/// <param name="AfterContainerId">The full container ID after restart.</param>
/// <param name="AfterImage">The configured image after restart.</param>
/// <param name="AfterImageId">The actual image ID after restart.</param>
/// <param name="AfterState">The verified running state after restart.</param>
/// <param name="BeforeStartedAt">The original Docker start timestamp.</param>
/// <param name="AfterStartedAt">The new Docker start timestamp.</param>
/// <param name="NewRuntimeStartConfirmed">The verified timestamp change.</param>
/// <param name="AspireStartSucceeded">The original successful native start-command assertion.</param>
/// <param name="AspireStartMessage">The actual native start-command message.</param>
/// <param name="SourceSha">The actual git HEAD output.</param>
/// <param name="RepositoryRoot">The unchanged source checkout path.</param>
internal sealed record ContainerRuntimeRestartReceipt(string Scenario, string ResourceName, string ContainerName, string BeforeContainerId,
    string BeforeImage, string BeforeImageId, string BeforeState, int DockerKillExitCode, string KillOutput, string KillError, string StoppedState,
    string AfterContainerId, string AfterImage, string AfterImageId, string AfterState, string BeforeStartedAt,
    string AfterStartedAt, bool NewRuntimeStartConfirmed, bool AspireStartSucceeded, string? AspireStartMessage, string? SourceSha, string RepositoryRoot)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LocalBuildInputDigest
    {
        get; init;
    }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LocalImageInvocationId
    {
        get; init;
    }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LocalImageConfigId
    {
        get; init;
    }
    public DateTimeOffset KillStartedAtUtc
    {
        get; init;
    }
    public DateTimeOffset KillCompletedAtUtc
    {
        get; init;
    }
}

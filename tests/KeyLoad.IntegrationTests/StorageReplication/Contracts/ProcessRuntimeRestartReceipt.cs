using KeyLoad.AppHost.TestInfrastructure;
using KeyLoad.Diagnostics.ResourceExecution;

namespace KeyLoad.IntegrationTests.StorageReplication;

/// <summary>Retains actual process, binary and compilation evidence around the original abrupt fault.</summary>
/// <param name="Scenario">The qualification scenario.</param>
/// <param name="ResourceName">The actual Aspire project resource.</param>
/// <param name="Before">The original live process evidence.</param>
/// <param name="Killed">The original joined process kill and exit.</param>
/// <param name="After">The observed replacement and its actual files.</param>
/// <param name="AspireStartSucceeded">Whether the one execution Start command was accepted.</param>
/// <param name="AspireStartMessage">The original execution command message.</param>
/// <param name="RepositoryRoot">The source checkout owning the artifact.</param>
internal sealed record ProcessRuntimeRestartReceipt(string Scenario, string ResourceName,
    ProcessEvidence Before, ProcessFaultCommandProtocol.Result Killed, ProcessEvidence After,
    bool AspireStartSucceeded, string? AspireStartMessage, string RepositoryRoot)
{
    public DateTimeOffset KillStartedAtUtc { get; init; }
    public DateTimeOffset KillCompletedAtUtc { get; init; }
}

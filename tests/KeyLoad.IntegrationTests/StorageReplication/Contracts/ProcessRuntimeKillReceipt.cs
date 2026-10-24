using KeyLoad.AppHost.TestInfrastructure;
using KeyLoad.Diagnostics.ResourceExecution;

namespace KeyLoad.IntegrationTests.StorageReplication;

/// <summary>Retains the original joined abrupt process kill pending a genuine Aspire restart.</summary>
/// <param name="Scenario">The qualification scenario.</param>
/// <param name="ResourceName">The actual execution Aspire project resource.</param>
/// <param name="Before">The observed live process and its actual compiled files.</param>
/// <param name="Killed">The exact process identity and exit returned by the original joined kill.</param>
internal sealed record ProcessRuntimeKillReceipt(string Scenario, string ResourceName,
    ProcessEvidence Before, ProcessFaultCommandProtocol.Result Killed)
{
    public DateTimeOffset KillStartedAtUtc { get; init; }
    public DateTimeOffset KillCompletedAtUtc { get; init; }
}

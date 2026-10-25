using System.Text;
using KeyLoad.AppHost.TestInfrastructure;
using KeyLoad.Diagnostics.ResourceExecution;

namespace KeyLoad.ClusterTests.StorageReplication;

/// <summary>Names the actual execution resource lifecycle and owned restart artifacts.</summary>
internal static class ProcessRuntimeProtocol
{
    internal const string ArtifactsDirectory = "artifacts";
    internal const string QualificationDirectory = "qualification";
    internal static readonly CompositeFormat ReceiptFileName = CompositeFormat.Parse("cluster-process-restart-{0}-{1}.json");
    internal static readonly CompositeFormat MissingKillFailure = CompositeFormat.Parse("No verified process kill receipt exists for Aspire resource '{0}'.");
    internal static readonly CompositeFormat StartFailure = CompositeFormat.Parse("Aspire failed to start '{0}': {1}");
    internal const string UnknownCommandFailure = "unknown command failure";
    internal static readonly CompositeFormat MissingProcessFailure = CompositeFormat.Parse("Aspire model has no execution project resource '{0}'.");
    internal const string ReplacedFailure = "The accepted execution restart replacement identity changed before settlement.";

    internal static ProcessFaultCommandProtocol.Identity Identity(ProcessEvidence evidence)
        => new(evidence.Name, evidence.ProcessId,
            (evidence.AspireStartedAtUtc ?? throw new InvalidOperationException(ReplacedFailure)).Ticks,
            evidence.StartedAtUtc.Ticks,
            evidence.AspireProcessId ?? throw new InvalidOperationException(ReplacedFailure),
            (evidence.AspireProcessStartedAtUtc ?? throw new InvalidOperationException(ReplacedFailure)).Ticks);
}

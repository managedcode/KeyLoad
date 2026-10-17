using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal sealed class NativeCapabilityOperationFailureEvidence
{
    private const string DirectoryName = "capability-original-operation";
    private const string OriginalDirectory = "original";
    private const string TerminalDirectory = "terminal";
    private const string IdentityFormat = "N";

    private readonly Guid session = Guid.NewGuid();
    private bool originalAttempted;

    internal void Original(string phase, bool official, IReadOnlyList<Exception> failures,
        CancellationToken timeout, CancellationToken lifetime, CancellationToken caller)
    {
        if (originalAttempted)
        {
            return;
        }
        originalAttempted = true;
        Save(phase, official, terminal: false, failures, timeout, lifetime, caller);
    }

    internal void Terminal(string phase, bool official, IReadOnlyList<Exception> failures,
        CancellationToken timeout, CancellationToken lifetime, CancellationToken caller)
        => Save(phase, official, terminal: true, failures, timeout, lifetime, caller);

    private void Save(string phase, bool official, bool terminal, IReadOnlyList<Exception> failures,
        CancellationToken timeout, CancellationToken lifetime, CancellationToken caller)
    {
        var root = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            ClusterFixtureProtocol.ArtifactDirectory, ClusterFixtureProtocol.QualificationDirectory,
            DirectoryName, session.ToString(IdentityFormat), terminal ? TerminalDirectory : OriginalDirectory);
        var retained = failures.ToArray();
        var lines = new[]
        {
            $"OriginalCapabilityOperationStage={phase}; OfficialMcp={official}; Terminal={terminal}",
            $"TimeoutCanceled={timeout.IsCancellationRequested}; LifetimeCanceled={lifetime.IsCancellationRequested}; CallerCanceled={caller.IsCancellationRequested}"
        }.Concat(retained.Select(failure => RemoteTransferStartupFailureLines.Read(failure).First()))
            .Concat(retained.SelectMany(RemoteTransferStartupFailureLines.Read));
        Directory.CreateDirectory(root);
        _ = ClusterFailureReceipts.SaveImmutable(root, BoundedDiagnosticLog.Bound(lines));
    }
}

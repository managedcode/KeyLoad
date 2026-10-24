using Aspire.Hosting;

namespace KeyLoad.IntegrationTests.StorageReplication;

/// <summary>Captures safe Aspire state and actual execution process identity after a restart failure.</summary>
internal sealed class ProcessRestartFailureCapture
{
    private const string StartSucceededLine = "Aspire Start command succeeded.";
    private const string StartRetainedLine = "Original accepted Aspire Start retained; no new Start command issued.";

    private readonly DistributedApplication app;
    private readonly ProcessRuntimeKillReceipt receipt;
    private readonly ClusterFailureReceipts failureReceipts;
    private readonly List<string> lines = new();
    private int snapshotSequence;

    /// <summary>Captures the pre-start public Aspire snapshot and verified pre-kill identity.</summary>
    /// <param name="app">The actual Aspire application for this Orleans fixture.</param>
    /// <param name="receipt">The verified SIGKILL receipt retained by the fixture.</param>
    /// <param name="repositoryRoot">The checkout root for qualification artifacts.</param>
    /// <param name="failureReceipts">The fixture-owned restart receipt store.</param>
    internal ProcessRestartFailureCapture(DistributedApplication app, ProcessRuntimeKillReceipt receipt,
        string repositoryRoot, ClusterFailureReceipts failureReceipts)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(failureReceipts);
        this.app = app;
        this.receipt = receipt;
        this.failureReceipts = failureReceipts;
        AddSafeSnapshot(ProcessRestartSnapshotStage.BeforeStart);
        lines.Add(ProcessRestartDiagnostics.FormatVerifiedKill(receipt.Before));
    }

    /// <summary>Captures the public Aspire state after execution Start succeeds without changing its result.</summary>
    internal void StartSucceeded()
    {
        lines.Add(StartSucceededLine);
        AddSafeSnapshot(ProcessRestartSnapshotStage.AfterStartSucceeded);
    }

    /// <summary>Records actual retained acceptance without manufacturing a new Start result.</summary>
    internal void StartRetained()
    {
        lines.Add(StartRetainedLine);
        AddSafeSnapshot(ProcessRestartSnapshotStage.AcceptedStartRetained);
    }

    /// <summary>Captures the original failure and the bounded execution resource snapshot.</summary>
    /// <param name="stage">The closed restart stage that failed.</param>
    /// <param name="failure">The original failure; only its closed type category is retained.</param>
    internal Task SaveAsync(ProcessRestartStage stage, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        AddSafeSnapshot(ProcessRestartSnapshotStage.Failure);
        lines.Add(ProcessRestartDiagnostics.FormatFailure(stage, failure));

        var output = failureReceipts.RequireRunOutput(TestContext.ResultsDirectory);
        Directory.CreateDirectory(output);
        failureReceipts.Save(output, BoundedDiagnosticLog.Bound(lines));
        return Task.CompletedTask;
    }

    private void AddSafeSnapshot(ProcessRestartSnapshotStage stage)
    {
        try
        {
            app.ResourceNotifications.TryGetCurrentState(receipt.ResourceName, out var current);
            lines.Add(ProcessRestartDiagnostics.FormatResourceSnapshot(stage, ++snapshotSequence, current));
        }
        catch (Exception error) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(error))
        {
            lines.Add(ProcessRestartDiagnostics.FormatResourceSnapshot(stage, ++snapshotSequence, null));
        }
    }
}

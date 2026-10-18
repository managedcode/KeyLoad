using Aspire.Hosting;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Samples safe Aspire state and actual Docker identity after a restart failure.</summary>
internal sealed class ContainerRestartFailureCapture
{
    private const string StartSucceededLine = "Aspire Start command succeeded.";
    private const string StartRetainedLine = "Original accepted Aspire Start retained; no new Start command issued.";

    private readonly DistributedApplication app;
    private readonly ContainerRuntimeKillReceipt receipt;
    private readonly ClusterFailureReceipts failureReceipts;
    private readonly List<string> lines = new();
    private int sampleSequence;

    /// <summary>Captures the pre-start public Aspire snapshot and verified pre-kill identity.</summary>
    /// <param name="app">The actual Aspire application for this CLUSTER fixture.</param>
    /// <param name="receipt">The verified SIGKILL receipt retained by the fixture.</param>
    /// <param name="repositoryRoot">The checkout root for qualification artifacts.</param>
    /// <param name="failureReceipts">The fixture-owned restart receipt store.</param>
    internal ContainerRestartFailureCapture(DistributedApplication app, ContainerRuntimeKillReceipt receipt,
        string repositoryRoot, ClusterFailureReceipts failureReceipts)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(failureReceipts);
        this.app = app;
        this.receipt = receipt;
        this.failureReceipts = failureReceipts;
        AddSafeSample(ContainerRestartSampleStage.BeforeStart);
        lines.Add(ContainerRestartDiagnostics.FormatVerifiedKill(receipt.Before));
    }

    /// <summary>Samples the public Aspire state after native Start succeeds without changing its result.</summary>
    internal void StartSucceeded()
    {
        lines.Add(StartSucceededLine);
        AddSafeSample(ContainerRestartSampleStage.AfterStartSucceeded);
    }

    /// <summary>Records actual retained acceptance without manufacturing a new Start result.</summary>
    internal void StartRetained()
    {
        lines.Add(StartRetainedLine);
        AddSafeSample(ContainerRestartSampleStage.AcceptedStartRetained);
    }

    /// <summary>Captures the failure snapshot and one bounded read-only Docker inspection.</summary>
    /// <param name="stage">The closed restart stage that failed.</param>
    /// <param name="failure">The original failure; only its closed type category is retained.</param>
    internal async Task SaveAsync(ContainerRestartStage stage, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        AddSafeSample(ContainerRestartSampleStage.Failure);
        lines.Add(ContainerRestartDiagnostics.FormatFailure(stage, failure));
        lines.Add(await ContainerRestartDockerInspection.ReadAsync(receipt.ContainerName).ConfigureAwait(false));

        var output = failureReceipts.RequireRunOutput(TestContext.ResultsDirectory);
        Directory.CreateDirectory(output);
        failureReceipts.Save(output, BoundedDiagnosticLog.Bound(lines));
    }

    private void AddSafeSample(ContainerRestartSampleStage stage)
    {
        try
        {
            app.ResourceNotifications.TryGetCurrentState(receipt.ResourceName, out var current);
            lines.Add(ContainerRestartDiagnostics.FormatResourceSample(stage, ++sampleSequence, current));
        }
        catch (Exception error) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(error))
        {
            lines.Add(ContainerRestartDiagnostics.FormatResourceSample(stage, ++sampleSequence, null));
        }
    }
}

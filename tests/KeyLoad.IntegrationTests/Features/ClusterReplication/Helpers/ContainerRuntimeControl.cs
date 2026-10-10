using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.ClusterReplicationTestSupport;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Coordinates genuine Docker SIGKILL and Aspire restart with retained qualification receipts.</summary>
/// <param name="app">The actual fixture-owned distributed application.</param>
/// <param name="containerNames">Explicit container names selected from the Aspire model.</param>
/// <param name="repositoryRoot">The source checkout recorded with each restart receipt.</param>
/// <param name="localImageSelection">The same verified fixture-owned local image selection, when present.</param>
internal sealed class ContainerRuntimeControl(
    DistributedApplication app,
    IReadOnlyDictionary<string, string> containerNames,
    string repositoryRoot, LocalRf3ImageSelection.Selection? localImageSelection = null)
{
    private readonly Dictionary<string, ContainerRestartOwnership> restartOwners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ContainerRuntimeKillReceipt> pendingReceipts = new(StringComparer.Ordinal);
    private readonly ClusterFailureReceipts restartFailureReceipts = new(ClusterFailureReceiptKind.Restart);
    private const string RestartDiagnosticsFailureKey = "KeyLoad.Rf3RestartDiagnosticsFailure";

    /// <summary>Kills the inspected full container ID and waits until the same named container is exited.</summary>
    /// <param name="resourceName">The actual Aspire resource to kill.</param>
    /// <param name="scenario">The unchanged qualification receipt scenario.</param>
    /// <param name="cancellationToken">The existing external operation cancellation.</param>
    public async Task KillAsync(string resourceName, string scenario, CancellationToken cancellationToken)
    {
        if (pendingReceipts.ContainsKey(resourceName))
        { throw new InvalidOperationException(ContainerRestartOwnership.PendingFailure); }
        var containerName = GetContainerName(resourceName);
        var before = await ContainerRuntimeDocker.InspectAsync(containerName, cancellationToken);
        if (before.State != ContainerRuntimeProtocol.RunningState)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                ContainerRuntimeProtocol.BeforeKillFailure, resourceName, before.State));
        }

        var killStartedAtUtc = TimeProvider.System.GetUtcNow();
        var kill = await ContainerRuntimeDocker.RunAsync(
            [ContainerRuntimeProtocol.KillCommand, ContainerRuntimeProtocol.SignalArgument,
                ContainerRuntimeProtocol.KillSignal, before.Id], cancellationToken);
        ContainerRuntimeDocker.EnsureSuccessful(kill, ContainerRuntimeProtocol.KillOperation, resourceName);
        var killCompletedAtUtc = TimeProvider.System.GetUtcNow();
        var stopped = await ContainerRuntimeDocker.WaitForExitedAsync(containerName, resourceName, cancellationToken);
        pendingReceipts[resourceName] = new(scenario, resourceName, containerName, before, stopped,
            kill.ExitCode, kill.StandardOutput, kill.StandardError)
        {
            KillStartedAtUtc = killStartedAtUtc,
            KillCompletedAtUtc = killCompletedAtUtc
        };
    }

    /// <summary>Starts through Aspire, awaits healthy/running state and records a changed runtime start timestamp.</summary>
    /// <param name="resourceName">The actual Aspire resource with a retained verified kill receipt.</param>
    /// <param name="cancellationToken">The existing external operation cancellation.</param>
    public async Task RestartAsync(string resourceName, CancellationToken cancellationToken)
    {
        if (!pendingReceipts.TryGetValue(resourceName, out var receipt))
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                ContainerRuntimeProtocol.MissingKillFailure, resourceName));
        }

        var capture = new ContainerRestartFailureCapture(app, receipt, repositoryRoot, restartFailureReceipts);
        var progress = new RestartProgress();
        try
        {
            await RestartAndRecordAsync(receipt, capture, progress, cancellationToken);
            pendingReceipts.Remove(resourceName);
            restartOwners.Remove(resourceName);
        }
        catch (Exception failure) when (IsNonFatalCleanupFailure(failure))
        {
            await SaveRestartFailureAsync(capture, progress.Stage, failure);
            throw;
        }
    }

    private async Task RestartAndRecordAsync(ContainerRuntimeKillReceipt receipt,
        ContainerRestartFailureCapture capture, RestartProgress progress, CancellationToken cancellationToken)
    {
        var resourceName = receipt.ResourceName;
        var owner = RequireRestartOwner(resourceName);
        await owner.AcceptAsync(app, receipt, capture, cancellationToken);
        progress.Stage = ContainerRestartStage.HealthWait;
        cancellationToken.ThrowIfCancellationRequested();
        await app.ResourceNotifications.WaitForResourceHealthyAsync(resourceName,
            WaitBehavior.WaitOnResourceUnavailable, cancellationToken);
        progress.Stage = ContainerRestartStage.RuntimeInspection;
        var after = await ContainerRuntimeDocker.InspectRunningAsync(receipt.ContainerName, resourceName, cancellationToken);
        owner.RequireSameReplacement(after);
        progress.Stage = ContainerRestartStage.IdentityValidation;
        var startedAtChanged = !string.Equals(receipt.Before.StartedAt, after.StartedAt, StringComparison.Ordinal);
        if (!startedAtChanged)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                ContainerRuntimeProtocol.UnchangedStartFailure, resourceName));
        }

        progress.Stage = ContainerRestartStage.SourceReceipt;
        var source = await ContainerRuntimeReceiptStore.ReadSourceIdentityAsync(repositoryRoot, receipt.Before, after,
            localImageSelection, cancellationToken);
        var completed = new ContainerRuntimeRestartReceipt(receipt.Scenario, resourceName, receipt.ContainerName, receipt.Before.Id,
            receipt.Before.ConfigImage, receipt.Before.ImageId, receipt.Before.State, receipt.KillExitCode, receipt.KillOutput, receipt.KillError,
            receipt.Stopped.State, after.Id, after.ConfigImage, after.ImageId, after.State, receipt.Before.StartedAt,
            after.StartedAt, startedAtChanged, true, owner.StartMessage, source.SourceSha, repositoryRoot)
        {
            KillStartedAtUtc = receipt.KillStartedAtUtc,
            KillCompletedAtUtc = receipt.KillCompletedAtUtc,
            LocalBuildInputDigest = source.LocalBuildInputDigest,
            LocalImageInvocationId = source.LocalImageInvocationId,
            LocalImageConfigId = source.LocalImageConfigId
        };
        await ContainerRuntimeReceiptStore.WriteAsync(completed, cancellationToken);
    }

    /// <summary>Accepts one native Start and retains its inspected replacement before health settlement.</summary>
    /// <param name="resourceName">The owned resource with an unsettled verified kill.</param>
    /// <param name="cancellationToken">The original bounded owner token.</param>
    /// <returns>The actual running replacement identity retained for subsequent settlement.</returns>
    internal async Task<ContainerRuntimeInspection> BeginRestartAsync(string resourceName,
        CancellationToken cancellationToken)
    {
        if (!pendingReceipts.TryGetValue(resourceName, out var receipt))
        { throw new InvalidOperationException(ContainerRestartOwnership.PendingFailure); }
        var capture = new ContainerRestartFailureCapture(app, receipt, repositoryRoot, restartFailureReceipts);
        var owner = RequireRestartOwner(resourceName);
        try
        { return await owner.AcceptAsync(app, receipt, capture, cancellationToken); }
        catch (Exception failure) when (IsNonFatalCleanupFailure(failure))
        {
            var stage = owner.StartAccepted ? ContainerRestartStage.RuntimeInspection : ContainerRestartStage.StartCommand;
            await SaveRestartFailureAsync(capture, stage, failure);
            throw;
        }
    }

    private ContainerRestartOwnership RequireRestartOwner(string resourceName)
    {
        if (!restartOwners.TryGetValue(resourceName, out var owner))
        {
            owner = new ContainerRestartOwnership();
            restartOwners.Add(resourceName, owner);
        }
        return owner;
    }

    private static async Task SaveRestartFailureAsync(ContainerRestartFailureCapture capture,
        ContainerRestartStage stage, Exception failure)
    {
        try
        { await capture.SaveAsync(stage, failure); }
        catch (Exception diagnosticFailure) when (IsNonFatalCleanupFailure(diagnosticFailure))
        { failure.Data[RestartDiagnosticsFailureKey] = diagnosticFailure; }
    }

    internal IReadOnlyDictionary<string, ContainerRuntimeKillReceipt> RequireKilledOwners(DistributedApplication originalOwner)
        => ContainerRuntimeKilledOwnerValidation.Require(app, originalOwner, containerNames, pendingReceipts);

    private string GetContainerName(string resourceName) => containerNames.TryGetValue(resourceName, out var name)
        ? name
        : throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
            ContainerRuntimeProtocol.MissingContainerFailure, resourceName));

    private sealed class RestartProgress
    {
        internal ContainerRestartStage Stage { get; set; } = ContainerRestartStage.StartCommand;
    }
}

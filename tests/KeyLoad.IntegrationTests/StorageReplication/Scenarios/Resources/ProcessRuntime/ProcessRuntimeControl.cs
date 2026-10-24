using System.Collections.Concurrent;
using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.TestInfrastructure;
using static KeyLoad.IntegrationTests.StorageReplication.ClusterReplicationTestSupport;

namespace KeyLoad.IntegrationTests.StorageReplication;

/// <summary>Coordinates actual abrupt execution process faults and one retained Aspire restart per fault.</summary>
internal sealed class ProcessRuntimeControl(DistributedApplication app, IReadOnlyCollection<string> resourceNames,
    string repositoryRoot)
{
    private readonly ConcurrentDictionary<string, ProcessRestartOwnership> restartOwners = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ProcessRuntimeKillReceipt> pendingReceipts = new(StringComparer.Ordinal);
    private readonly ClusterFailureReceipts restartFailureReceipts = new(ClusterFailureReceiptKind.Restart);
    private const string RestartDiagnosticsFailureKey = "KeyLoad.ClusterRestartDiagnosticsFailure";

    internal async Task KillAsync(string resourceName, string scenario, CancellationToken cancellationToken)
    {
        RequireResource(resourceName);
        if (pendingReceipts.ContainsKey(resourceName))
        {
            throw new InvalidOperationException(ProcessRestartOwnership.PendingFailure);
        }
        var before = await ClusterFixtureApplicationStartup.ReadAsync(app, resourceName, cancellationToken);
        var started = TimeProvider.System.GetUtcNow();
        var killed = await AspireProcessFaultCommand.KillAsync(app, ProcessRuntimeProtocol.Identity(before), cancellationToken);
        var receipt = new ProcessRuntimeKillReceipt(scenario, resourceName, before, killed)
        {
            KillStartedAtUtc = started,
            KillCompletedAtUtc = TimeProvider.System.GetUtcNow()
        };
        if (!pendingReceipts.TryAdd(resourceName, receipt))
        {
            throw new InvalidOperationException(ProcessRestartOwnership.PendingFailure);
        }
    }

    internal async Task RestartAsync(string resourceName, CancellationToken cancellationToken)
    {
        if (!pendingReceipts.TryGetValue(resourceName, out var receipt))
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                ProcessRuntimeProtocol.MissingKillFailure, resourceName));
        }
        var capture = new ProcessRestartFailureCapture(app, receipt, repositoryRoot, restartFailureReceipts);
        var progress = new RestartProgress();
        try
        {
            await RestartAndRecordAsync(receipt, capture, progress, cancellationToken);
            pendingReceipts.TryRemove(resourceName, out _);
            restartOwners.TryRemove(resourceName, out _);
        }
        catch (Exception failure) when (IsNonFatalCleanupFailure(failure))
        {
            await SaveRestartFailureAsync(capture, progress.Stage, failure);
            throw;
        }
    }

    private async Task RestartAndRecordAsync(ProcessRuntimeKillReceipt receipt,
        ProcessRestartFailureCapture capture, RestartProgress progress, CancellationToken cancellationToken)
    {
        var resourceName = receipt.ResourceName;
        var owner = RequireRestartOwner(resourceName);
        await owner.AcceptAsync(app, receipt, capture, cancellationToken);
        progress.Stage = ProcessRestartStage.HealthWait;
        cancellationToken.ThrowIfCancellationRequested();
        await app.ResourceNotifications.WaitForResourceHealthyAsync(resourceName,
            WaitBehavior.WaitOnResourceUnavailable, cancellationToken);
        progress.Stage = ProcessRestartStage.RuntimeInspection;
        var after = await ClusterFixtureApplicationStartup.ReadAsync(app, resourceName, cancellationToken);
        owner.RequireSameReplacement(ProcessRuntimeProtocol.Identity(after).Launch);
        progress.Stage = ProcessRestartStage.IdentityValidation;
        if (after.Binary != receipt.Before.Binary)
        {
            throw new InvalidOperationException(ProcessRuntimeProtocol.ReplacedFailure);
        }
        progress.Stage = ProcessRestartStage.SourceReceipt;
        var completed = new ProcessRuntimeRestartReceipt(receipt.Scenario, resourceName, receipt.Before,
            receipt.Killed, after, true, owner.StartMessage, repositoryRoot)
        {
            KillStartedAtUtc = receipt.KillStartedAtUtc,
            KillCompletedAtUtc = receipt.KillCompletedAtUtc
        };
        await ProcessRuntimeReceiptStore.WriteAsync(completed, cancellationToken);
    }

    internal async Task<ProcessFaultCommandProtocol.LaunchIdentity> BeginRestartAsync(string resourceName,
        CancellationToken cancellationToken)
    {
        if (!pendingReceipts.TryGetValue(resourceName, out var receipt))
        {
            throw new InvalidOperationException(ProcessRestartOwnership.PendingFailure);
        }
        var capture = new ProcessRestartFailureCapture(app, receipt, repositoryRoot, restartFailureReceipts);
        var owner = RequireRestartOwner(resourceName);
        try
        {
            return await owner.AcceptAsync(app, receipt, capture, cancellationToken);
        }
        catch (Exception failure) when (IsNonFatalCleanupFailure(failure))
        {
            var stage = owner.StartAccepted ? ProcessRestartStage.RuntimeInspection : ProcessRestartStage.StartCommand;
            await SaveRestartFailureAsync(capture, stage, failure);
            throw;
        }
    }

    private ProcessRestartOwnership RequireRestartOwner(string resourceName)
        => restartOwners.GetOrAdd(resourceName, static _ => new ProcessRestartOwnership());

    private static async Task SaveRestartFailureAsync(ProcessRestartFailureCapture capture,
        ProcessRestartStage stage, Exception failure)
    {
        try
        {
            await capture.SaveAsync(stage, failure);
        }
        catch (Exception diagnosticFailure) when (IsNonFatalCleanupFailure(diagnosticFailure))
        {
            failure.Data[RestartDiagnosticsFailureKey] = diagnosticFailure;
        }
    }

    internal IReadOnlyDictionary<string, ProcessRuntimeKillReceipt> RequireKilledOwners(DistributedApplication originalOwner)
    {
        var original = pendingReceipts.ToArray().ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        return ProcessRuntimeKilledOwnerValidation.Require(app, originalOwner, resourceNames, original);
    }

    private void RequireResource(string resourceName)
    {
        if (!resourceNames.Contains(resourceName, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                ProcessRuntimeProtocol.MissingProcessFailure, resourceName));
        }
    }

    private sealed class RestartProgress
    {
        internal ProcessRestartStage Stage { get; set; } = ProcessRestartStage.StartCommand;
    }
}

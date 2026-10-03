using System.Diagnostics;
using KeyLoad.CrashHost;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class EpochUpgradeProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-epoch-upgrade-crash-";
    private const int TimeoutSeconds = 45;
    private const int CleanupSeconds = 30;
    private const int SourceArgument = 0;
    private const int TargetArgument = 1;
    private const int StageArgument = 2;
    private const int ModeArgument = 3;
    private const string CleanupFailureKey = "KeyLoad.EpochUpgradeCleanupFailure";

    [Test]
    [Arguments(CommitStage.UpgradeSourceVerified, false)]
    [Arguments(CommitStage.UpgradePrepared, false)]
    [Arguments(CommitStage.UpgradeRecovered, false)]
    [Arguments(CommitStage.UpgradeCheckpointFlushed, false)]
    [Arguments(CommitStage.UpgradePublished, false)]
    [Arguments(CommitStage.UpgradeSourceVerified, true)]
    [Arguments(CommitStage.UpgradePrepared, true)]
    [Arguments(CommitStage.UpgradeRecovered, true)]
    [Arguments(CommitStage.UpgradeCheckpointFlushed, true)]
    [Arguments(CommitStage.UpgradePublished, true)]
    public async Task AcEpoch001To006OfflineUpgradeCrashRetainsSourceAndPublishesOnlyCompleteCurrentTarget(
        CommitStage stage, bool compacted)
    {
        await RunTrialAsync(stage, compacted, TestContext.Current!.Execution.CancellationToken);
    }

    private static async Task RunTrialAsync(CommitStage stage, bool compacted, CancellationToken callerToken)
    {
        var root = Path.Combine(Path.GetTempPath(), TrialPrefix + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var target = Path.Combine(root, "target");
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        Process? process = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        Exception? activeFailure = null;
        try
        {
            Directory.CreateDirectory(root);
            var receipt = await EpochPriorExecutableFixture.CreateAsync(source, compacted, timeout.Token);
            var sourceInventory = await EpochUpgradeFileInventory.CaptureAsync(source, timeout.Token);
            process = StartCrashProcess(source, target, stage);
            await AwaitCrashBoundaryAsync(process, timeout.Token);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(timeout.Token);
            await KilledProcessFileReadiness.WaitAsync(source, timeout.Token);
            await RunRecoveryAssertionsAsync(source, target, stage, receipt, sourceInventory, timeout.Token);
        }
        catch (Exception failure)
        {
            activeFailure = failure;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupSeconds));
            try
            {
                await CleanupAsync(process, root, source, cleanup.Token);
            }
            catch (Exception cleanupFailure)
            {
                if (activeFailure is null)
                {
                    throw;
                }
                activeFailure.Data[CleanupFailureKey] = cleanupFailure;
            }
        }
    }

    private static async Task RunRecoveryAssertionsAsync(string source, string target, CommitStage stage,
        EpochPriorProbeReceipt receipt, Dictionary<string, string> sourceInventory,
        CancellationToken cancellationToken)
    {
        await EpochUpgradeRecoveryAssertions.VerifyAfterCrashAsync(source, target, stage, receipt,
            sourceInventory, cancellationToken);
        var firstRetry = EpochUpgradeRecoveryAssertions.UpgradeRetryAndVerify(source, target, receipt);
        await EpochUpgradeRecoveryAssertions.AssertIdentityAsync(firstRetry, receipt);
        await EpochUpgradeFileInventory.AssertUnchangedAsync(source, sourceInventory, cancellationToken);
        await VerifyPublishedRetryAsync(source, target, receipt, sourceInventory, cancellationToken);
    }

    private static async Task VerifyPublishedRetryAsync(string source, string target, EpochPriorProbeReceipt receipt,
        Dictionary<string, string> sourceInventory, CancellationToken cancellationToken)
    {
        var targetAfterWrite = await EpochUpgradeRecoveryAssertions.WriteAndCaptureLaterTargetAsync(
            target, receipt, cancellationToken);
        var repeatedRetry = EpochUpgradeRecoveryAssertions.RetryPublishedTarget(source, target);
        await EpochUpgradeRecoveryAssertions.AssertIdentityAsync(repeatedRetry, receipt);
        await EpochUpgradeRecoveryAssertions.AssertLaterTargetWasPreservedAsync(target, receipt,
            targetAfterWrite, cancellationToken);
        await EpochUpgradeFileInventory.AssertUnchangedAsync(source, sourceInventory, cancellationToken);
        await Assert.That(Directory.Exists(target + ".upgrade")).IsFalse();
    }

    private static Process StartCrashProcess(string source, string target, CommitStage stage)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[]
        {
            typeof(CrashHostMarker).Assembly.Location,
            source,
            target,
            stage.ToString(),
            EpochUpgradeCrashScenario.Mode
        })
        {
            start.ArgumentList.Add(argument);
        }
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start epoch-upgrade CrashHost.");
    }

    private static async Task AwaitCrashBoundaryAsync(Process process, CancellationToken cancellationToken)
    {
        var marker = await process.StandardOutput.ReadLineAsync(cancellationToken);
        if (!string.Equals(marker, CrashFixtureValues.CrashMarker, StringComparison.Ordinal))
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException("The epoch-upgrade process did not reach its requested stage: " + error);
        }
    }

    private static async Task CleanupAsync(Process? process, string root, string source,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        if (process is not null)
        {
            if (!process.HasExited)
            {
                Capture(() => process.Kill(entireProcessTree: true), failures);
                await CaptureAsync(() => process.WaitForExitAsync(cancellationToken), failures);
            }
            Capture(process.Dispose, failures);
        }
        if (Directory.Exists(source))
        {
            await CaptureAsync(() => KilledProcessFileReadiness.WaitAsync(source, cancellationToken), failures);
            Capture(() => EpochUpgradeFileInventory.AssertNativeHandlesReleased(source), failures);
        }
        if (Directory.Exists(root))
        {
            await CaptureAsync(() => StoragePublicationRecoveryTests.DeleteTrialAsync(root, cancellationToken), failures);
        }
        if (failures.Count == 1)
        {
            throw failures[0];
        }
        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }
    }

    private static void Capture(Action action, List<Exception> failures)
    {
        try { action(); }
        catch (Exception failure) { failures.Add(failure); }
    }

    private static async Task CaptureAsync(Func<Task> action, List<Exception> failures)
    {
        try { await action(); }
        catch (Exception failure) { failures.Add(failure); }
    }
}

using System.Diagnostics;
using KeyLoad.CrashHost;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class EpochUpgradeProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-epoch-upgrade-crash-";
    private const int TimeoutSeconds = 45;
    private const int CleanupSeconds = 30;

    [Test]
    [Arguments(CommitStage.UpgradeSourceVerified, false, 5)]
    [Arguments(CommitStage.UpgradePrepared, false, 5)]
    [Arguments(CommitStage.UpgradeRecovered, false, 5)]
    [Arguments(CommitStage.UpgradeCheckpointFlushed, false, 5)]
    [Arguments(CommitStage.UpgradePublished, false, 5)]
    [Arguments(CommitStage.UpgradeSourceVerified, true, 5)]
    [Arguments(CommitStage.UpgradePrepared, true, 5)]
    [Arguments(CommitStage.UpgradeRecovered, true, 5)]
    [Arguments(CommitStage.UpgradeCheckpointFlushed, true, 5)]
    [Arguments(CommitStage.UpgradePublished, true, 5)]
    [Arguments(CommitStage.UpgradeSourceVerified, false, 6)]
    [Arguments(CommitStage.UpgradePrepared, false, 6)]
    [Arguments(CommitStage.UpgradeRecovered, false, 6)]
    [Arguments(CommitStage.UpgradeCheckpointFlushed, false, 6)]
    [Arguments(CommitStage.UpgradePublished, false, 6)]
    [Arguments(CommitStage.UpgradeSourceVerified, true, 6)]
    [Arguments(CommitStage.UpgradePrepared, true, 6)]
    [Arguments(CommitStage.UpgradeRecovered, true, 6)]
    [Arguments(CommitStage.UpgradeCheckpointFlushed, true, 6)]
    [Arguments(CommitStage.UpgradePublished, true, 6)]
    public async Task AcEpoch7OfflineUpgradeCrashRetainsSourceAndPublishesOnlyCompleteCurrentTarget(
        CommitStage stage, bool compacted, int dataEpoch)
    {
        await RunTrialAsync(stage, compacted, dataEpoch, TestContext.Current!.Execution.CancellationToken);
    }

    private static async Task RunTrialAsync(CommitStage stage, bool compacted, int dataEpoch,
        CancellationToken callerToken)
    {
        var root = Path.Combine(Path.GetTempPath(), TrialPrefix + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var target = Path.Combine(root, "target");
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        Process? process = null;
        using var timeoutTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds), TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeoutTimeout.Token);
        Exception? activeFailure = null;
        try
        {
            Directory.CreateDirectory(root);
            var receipt = await EpochPriorExecutableFixture.CreateAsync(source, compacted, timeout.Token, dataEpoch);
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
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupSeconds), TimeProvider.System);
            await EpochUpgradeCleanup.SettleAsync(process, root, source, activeFailure, cleanup.Token);
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

}

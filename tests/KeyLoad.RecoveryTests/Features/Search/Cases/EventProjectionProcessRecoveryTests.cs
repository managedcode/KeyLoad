using System.Diagnostics;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.Search;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Search;

internal sealed class EventProjectionProcessRecoveryTests
{
    private const int TimeoutSeconds = 45;
    private const int CleanupSeconds = 30;
    private const int MarkerOutputLimit = 8_192;

    [Test]
    [Arguments(CommitStage.HeaderWritten, 0)]
    [Arguments(CommitStage.PayloadWritten, 0)]
    [Arguments(CommitStage.JournalFlushed, 0)]
    [Arguments(CommitStage.MutationApplied, 0)]
    [Arguments(CommitStage.ApplyCompleted, 0)]
    public async Task AcLineage001002003ProjectionAndReceiptRecoverAsOneCanonicalCut(
        CommitStage stage, int mutationIndex)
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-event-projection-crash-" + Guid.NewGuid().ToString("N"));
        var callerToken = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        Process? process = null;
        using var timeoutTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds), TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeoutTimeout.Token);
        Exception? activeFailure = null;
        try
        {
            Directory.CreateDirectory(root);
            process = StartCrashProcess(root, stage, mutationIndex);
            await AwaitCrashBoundaryAsync(process, timeout.Token);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(timeout.Token);
            await JoinPipesAsync(process, timeout.Token);
            await KilledProcessFileReadiness.WaitAsync(root, timeout.Token);
            EpochUpgradeFileInventory.AssertNativeHandlesReleased(root);
            await EventProjectionProcessRecoveryAssertions.RecoverAndVerifyAsync(root, stage, timeout.Token);
        }
        catch (Exception failure)
        {
            activeFailure = failure;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupSeconds), TimeProvider.System);
            await EpochUpgradeCleanup.SettleAsync(process, root, root, activeFailure, cleanup.Token);
        }
    }

    private static Process StartCrashProcess(string root, CommitStage stage, int mutationIndex)
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
            root,
            stage.ToString(),
            mutationIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            EventProjectionCrashScenario.Mode
        })
        {
            start.ArgumentList.Add(argument);
        }
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start event projection CrashHost.");
    }

    private static async Task AwaitCrashBoundaryAsync(Process process, CancellationToken cancellationToken)
    {
        var marker = await process.StandardOutput.ReadLineAsync(cancellationToken);
        if (!string.Equals(marker, CrashFixtureValues.CrashMarker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The event projection process did not reach its requested commit boundary.");
        }
    }

    private static async Task JoinPipesAsync(Process process, CancellationToken cancellationToken)
    {
        await DrainBoundedAsync(process.StandardOutput, cancellationToken);
        await DrainBoundedAsync(process.StandardError, cancellationToken);
    }

    private static async Task DrainBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[512];
        var total = 0;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
        {
            total += read;
            if (total > MarkerOutputLimit)
            {
                throw new InvalidOperationException("The event projection CrashHost output exceeded its bound.");
            }
        }
    }
}

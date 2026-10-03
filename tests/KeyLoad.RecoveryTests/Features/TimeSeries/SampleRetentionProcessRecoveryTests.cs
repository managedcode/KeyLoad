using System.Diagnostics;
using System.Globalization;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.TimeSeries;

internal sealed class SampleRetentionProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-series-retention-crash-";
    private const int MutationIndex = 0;
    private const int TimeoutSeconds = 25;

    [Test]
    [Arguments(CommitStage.HeaderWritten)]
    [Arguments(CommitStage.PayloadWritten)]
    [Arguments(CommitStage.JournalFlushed)]
    [Arguments(CommitStage.MutationApplied)]
    [Arguments(CommitStage.ApplyCompleted)]
    public async Task AcSeries013015016ProcessKillRecoversOldOrCompleteRetentionPage(CommitStage stage)
    {
        var root = Path.Combine(Path.GetTempPath(), TrialPrefix + Guid.NewGuid().ToString("N"));
        var callerToken = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        using var process = StartCrashProcess(root, stage);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        try
        {
            await Assert.That(await process.StandardOutput.ReadLineAsync(timeout.Token))
                .IsEqualTo(CrashFixtureValues.CrashMarker);
            process.Kill();
            await process.WaitForExitAsync(timeout.Token);
            await KilledProcessFileReadiness.WaitAsync(root, timeout.Token);
            await SampleRetentionRecoveryAssertions.VerifyRecoveredPageAsync(root, stage, timeout.Token);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await StopAndDeleteAsync(process, root, cleanup.Token);
        }
    }

    private static Process StartCrashProcess(string root, CommitStage stage)
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
            MutationIndex.ToString(CultureInfo.InvariantCulture),
            SampleRetentionCrashScenario.Mode
        })
        {
            start.ArgumentList.Add(argument);
        }
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start sample-retention CrashHost.");
    }

    private static async Task StopAndDeleteAsync(Process process, string root, CancellationToken cancellationToken)
    {
        if (!process.HasExited)
        {
            process.Kill();
            await process.WaitForExitAsync(cancellationToken);
        }
        if (Directory.Exists(root))
        {
            await StoragePublicationRecoveryTests.DeleteTrialAsync(root, cancellationToken);
        }
    }
}

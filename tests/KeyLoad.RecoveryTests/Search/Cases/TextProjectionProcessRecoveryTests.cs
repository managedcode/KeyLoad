using System.Diagnostics;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.RecoveryTests.Features.Search;

internal sealed class TextProjectionProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-execution-text-process-";
    private const string StoreDirectoryName = "canonical";
    private const string ReceiptFileName = "canonical-cut.bin";
    private const int TimeoutSeconds = 45;
    private const int CleanupSeconds = 30;

    [Test]
    [Arguments(TextFaultStage.OwnerFlushed, false)]
    [Arguments(TextFaultStage.PostingWritten, false)]
    [Arguments(TextFaultStage.InventoryFlushed, false)]
    [Arguments(TextFaultStage.ManifestPublished, false)]
    [Arguments(TextFaultStage.GenerationActivated, false)]
    [Arguments(TextFaultStage.OwnerFlushed, true)]
    [Arguments(TextFaultStage.PostingWritten, true)]
    [Arguments(TextFaultStage.InventoryFlushed, true)]
    [Arguments(TextFaultStage.ManifestPublished, true)]
    [Arguments(TextFaultStage.GenerationActivated, true)]
    public async Task AcFts003004005ProcessCutsPreserveCanonicalCutAndRebuildSearch(
        TextFaultStage stage, bool replacement)
        => await RunTrialAsync(stage, replacement, TestContext.Current!.Execution.CancellationToken);

    private static async Task RunTrialAsync(TextFaultStage stage, bool replacement,
        CancellationToken callerToken)
    {
        var root = Path.Combine(Path.GetTempPath(), TrialPrefix + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, StoreDirectoryName);
        var receiptPath = Path.Combine(root, ReceiptFileName);
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        Process? process = null;
        using var timeoutTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds), TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeoutTimeout.Token);
        Exception? activeFailure = null;
        try
        {
            Directory.CreateDirectory(root);
            process = StartCrashProcess(source, receiptPath, stage, replacement);
            await TextCrashMarkerReader.AwaitAsync(process.StandardOutput, process.StandardError,
                timeout.Token);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(timeout.Token);
            await JoinOutputReadersAsync(process, timeout.Token);
            await KilledProcessFileReadiness.WaitAsync(source, timeout.Token);
            RecoveryFileInventory.AssertHandlesReleased(source);
            var receipt = OrleansSerialization.Deserialize<TextCrashReceipt>(
                await File.ReadAllBytesAsync(receiptPath, timeout.Token));
            await TextProjectionRecoveryAssertions.RecoverAndVerifyAsync(source, receipt, timeout.Token);
        }
        catch (Exception failure)
        {
            activeFailure = failure;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupSeconds), TimeProvider.System);
            await RecoveryProcessCleanup.SettleAsync(process, root, source, activeFailure, cleanup.Token);
        }
    }

    private static Process StartCrashProcess(string source, string receiptPath, TextFaultStage stage,
        bool replacement)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[]
        {
            typeof(CrashScenarioDispatcher).Assembly.Location, CrashFixtureValues.ProcessMode,
            source,
            receiptPath,
            stage.ToString(),
            replacement.ToString(),
            TextCrashScenario.Mode
        })
        {
            start.ArgumentList.Add(argument);
        }
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start execution text RecoveryProcess.");
    }

    private static async Task JoinOutputReadersAsync(Process process, CancellationToken cancellationToken)
    {
        await TextCrashMarkerReader.DrainAsync(process.StandardOutput, cancellationToken);
        await TextCrashMarkerReader.DrainAsync(process.StandardError, cancellationToken);
    }
}

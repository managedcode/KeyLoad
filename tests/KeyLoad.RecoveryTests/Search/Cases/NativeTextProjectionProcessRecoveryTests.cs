using System.Diagnostics;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.Search;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.RecoveryTests.Features.Search;

internal sealed class NativeTextProjectionProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-native-text-process-";
    private const string StoreDirectoryName = "canonical";
    private const string ReceiptFileName = "canonical-cut.bin";
    private const int TimeoutSeconds = 45;
    private const int CleanupSeconds = 30;

    [Test]
    [Arguments(NativeTextFaultStage.OwnerFlushed, false)]
    [Arguments(NativeTextFaultStage.NativePostingWritten, false)]
    [Arguments(NativeTextFaultStage.NativeInventoryFlushed, false)]
    [Arguments(NativeTextFaultStage.ManifestPublished, false)]
    [Arguments(NativeTextFaultStage.GenerationActivated, false)]
    [Arguments(NativeTextFaultStage.OwnerFlushed, true)]
    [Arguments(NativeTextFaultStage.NativePostingWritten, true)]
    [Arguments(NativeTextFaultStage.NativeInventoryFlushed, true)]
    [Arguments(NativeTextFaultStage.ManifestPublished, true)]
    [Arguments(NativeTextFaultStage.GenerationActivated, true)]
    public async Task AcFts003004005NativeProcessCutsPreserveCanonicalCutAndRebuildSearch(
        NativeTextFaultStage stage, bool replacement)
        => await RunTrialAsync(stage, replacement, TestContext.Current!.Execution.CancellationToken);

    private static async Task RunTrialAsync(NativeTextFaultStage stage, bool replacement,
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
            await NativeTextCrashMarkerReader.AwaitAsync(process.StandardOutput, process.StandardError,
                timeout.Token);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(timeout.Token);
            await JoinOutputReadersAsync(process, timeout.Token);
            await KilledProcessFileReadiness.WaitAsync(source, timeout.Token);
            RecoveryFileInventory.AssertNativeHandlesReleased(source);
            var receipt = NativeSerialization.Deserialize<NativeTextCrashReceipt>(
                await File.ReadAllBytesAsync(receiptPath, timeout.Token));
            await NativeTextProjectionRecoveryAssertions.RecoverAndVerifyAsync(source, receipt, timeout.Token);
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

    private static Process StartCrashProcess(string source, string receiptPath, NativeTextFaultStage stage,
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
            typeof(CrashHostMarker).Assembly.Location,
            source,
            receiptPath,
            stage.ToString(),
            replacement.ToString(),
            NativeTextCrashScenario.Mode
        })
        {
            start.ArgumentList.Add(argument);
        }
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start native text CrashHost.");
    }

    private static async Task JoinOutputReadersAsync(Process process, CancellationToken cancellationToken)
    {
        await NativeTextCrashMarkerReader.DrainAsync(process.StandardOutput, cancellationToken);
        await NativeTextCrashMarkerReader.DrainAsync(process.StandardError, cancellationToken);
    }
}

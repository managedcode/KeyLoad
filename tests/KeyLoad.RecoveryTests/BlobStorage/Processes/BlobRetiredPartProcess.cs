using System.Diagnostics;
using KeyLoad.RecoveryHost;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobRetiredPartProcess
{
    internal static async Task RunAsync(BlobRetiredPartFaultKind fault, CancellationToken callerToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        var root = Path.Combine(Path.GetTempPath(), "keyload-blob-retired-part-crash-" + Guid.NewGuid().ToString("N"));
        using var trial = new CancellationTokenSource(TimeSpan.FromSeconds(90), TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken, trial.Token);
        CommandIdempotencyProcessChild? child = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var seed = await BlobRetiredPartSeed.CreateAsync(root);
            child = CommandIdempotencyProcessChild.Create(8_192, CrashFixtureValues.CrashMarker);
            child.Start(StartInfo(root));
            child.ThrowStartupFailure();
            await child.WaitForAcknowledgementAsync(timeout.Token);
            await child.StopAndJoinAsync(timeout.Token);
            await KilledProcessFileReadiness.WaitAsync(root, timeout.Token);
            RecoveryFileInventory.AssertNativeHandlesReleased(root);
            child.CloseNativeProcessAfterJoin();
            await BlobRetiredPartTrial.RunAsync(root, seed, fault, timeout.Token);
        }
        catch (Exception failure) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            primary = failure;
        }
        catch (Exception failure) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(failure))
        {
            primary = failure;
        }
        await CleanupAsync(root, child, primary);
    }

    private static ProcessStartInfo StartInfo(string root)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { typeof(RecoveryHostMarker).Assembly.Location, root,
            CommitStage.HeaderWritten.ToString(), "0", QueueLifecycleCrashProtocol.Mode })
        {
            start.ArgumentList.Add(argument);
        }
        return start;
    }

    private static async Task CleanupAsync(string root, CommandIdempotencyProcessChild? child, Exception? primary)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30), TimeProvider.System);
        var failures = new List<Exception>();
        if (child is not null)
        {
            await CommandIdempotencyProcess.SettleActiveAsync(child, failures, timeout.Token);
        }
        if (child is null || child.IsSettled)
        {
            await CommandIdempotencyProcess.CleanTrialAsync(root, failures, timeout.Token);
        }
        else
        {
            failures.Add(new InvalidOperationException("The retired part process trial retained its root because its child did not settle."));
        }
        CommandIdempotencyProcess.ThrowFailures(primary, failures);
    }
}

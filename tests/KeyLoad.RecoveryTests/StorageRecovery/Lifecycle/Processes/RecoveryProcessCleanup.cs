using System.Diagnostics;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class RecoveryProcessCleanup
{
    private const int NoCleanupFailures = 0;
    private const string CleanupFailureKey = "KeyLoad.RecoveryProcessCleanupFailure";

    internal static async Task SettleAsync(Process? process, string root, string source,
        Exception? activeFailure, CancellationToken cancellationToken)
        => await SettleCoreAsync(process, root, source, activeFailure, cancellationToken);

    private static async Task SettleCoreAsync(Process? process, string root, string source,
        Exception? activeFailure, CancellationToken cancellationToken)
    {
        try
        {
            await CleanupAsync(process, root, source, activeFailure is null, cancellationToken);
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

    private static async Task CleanupAsync(Process? process, string root, string source,
        bool allowDelete, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var processSettled = process is null;
        if (process is not null)
        {
            await SettleProcessAsync(process, failures, cancellationToken);
            processSettled = ObserveHasExited(process, failures);
            if (processSettled)
            {
                Observe(process.Dispose, failures);
            }
            else
            {
                failures.Add(new IOException("The owned process did not exit; its trial root was retained."));
            }
        }
        if (processSettled && Directory.Exists(source))
        {
            await ObserveAsync(() => KilledProcessFileReadiness.WaitAsync(source, cancellationToken), failures);
            Observe(() => RecoveryFileInventory.AssertHandlesReleased(source), failures);
        }
        if (allowDelete && processSettled && failures.Count == NoCleanupFailures && Directory.Exists(root))
        {
            await ObserveAsync(() => StoragePublicationRecoveryTests.DeleteTrialAsync(root, cancellationToken), failures);
        }
        CommandIdempotencyProcessFailureHandling.ThrowFailures(failures);
    }

    private static async Task SettleProcessAsync(Process process, List<Exception> failures,
        CancellationToken cancellationToken)
    {
        if (ObserveHasExited(process, failures))
        {
            return;
        }
        Observe(() => process.Kill(entireProcessTree: true), failures);
        await ObserveAsync(() => process.WaitForExitAsync(cancellationToken), failures);
    }

    private static bool ObserveHasExited(Process process, List<Exception> failures)
    {
        var hasExited = false;
        Observe(() => hasExited = process.HasExited, failures);
        return hasExited;
    }

    private static void Observe(Action stage, List<Exception> failures)
        => ServerFailureObserver.Observe(stage, failures);

    private static Task ObserveAsync(Func<Task> stage, List<Exception> failures)
        => ServerFailureObserver.ObserveAsync(stage, failures);
}

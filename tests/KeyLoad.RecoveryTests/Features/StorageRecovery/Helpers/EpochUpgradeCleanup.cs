using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class EpochUpgradeCleanup
{
    private const string CleanupFailureKey = "KeyLoad.EpochUpgradeCleanupFailure";

    internal static async Task SettleAsync(Process? process, string root, string source,
        Exception? activeFailure, CancellationToken cancellationToken)
        => await SettleCoreAsync(process, root, source, nodeRoot: false, activeFailure, cancellationToken);

    internal static async Task SettleNodeAsync(Process? process, string root, string source,
        Exception? activeFailure, CancellationToken cancellationToken)
        => await SettleCoreAsync(process, root, source, nodeRoot: true, activeFailure, cancellationToken);

    private static async Task SettleCoreAsync(Process? process, string root, string source, bool nodeRoot,
        Exception? activeFailure, CancellationToken cancellationToken)
    {
        try
        {
            await CleanupAsync(process, root, source, nodeRoot, cancellationToken);
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

    private static async Task CleanupAsync(Process? process, string root, string source, bool nodeRoot,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var processSettled = process is null;
        var nodeReadinessPassed = !nodeRoot || !Directory.Exists(source);
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
            if (nodeRoot)
            {
                var readinessFailureCount = failures.Count;
                await ObserveAsync(() => NodeEpochFileReadiness.WaitAsync(source, cancellationToken), failures);
                nodeReadinessPassed = failures.Count == readinessFailureCount;
            }
            else
            {
                await ObserveAsync(() => KilledProcessFileReadiness.WaitAsync(source, cancellationToken), failures);
                Observe(() => EpochUpgradeFileInventory.AssertNativeHandlesReleased(source), failures);
            }
        }
        if (processSettled && nodeReadinessPassed && Directory.Exists(root))
        {
            await ObserveAsync(() => StoragePublicationRecoveryTests.DeleteTrialAsync(root, cancellationToken), failures);
        }
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }
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
        => ObserveAsync(() =>
        {
            stage();
            return Task.CompletedTask;
        }, failures).GetAwaiter().GetResult();

    private static async Task ObserveAsync(Func<Task> stage, List<Exception> failures)
    {
        async Task InvokeAsync() => await stage();
        var operation = InvokeAsync();
        await operation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (operation.Exception is { } fault)
        {
            failures.AddRange(fault.InnerExceptions);
        }
        else if (operation.IsCanceled)
        {
            failures.Add(new TaskCanceledException(operation));
        }
    }
}

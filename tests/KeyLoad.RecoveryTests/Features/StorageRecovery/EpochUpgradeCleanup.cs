using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class EpochUpgradeCleanup
{
    private const string CleanupFailureKey = "KeyLoad.EpochUpgradeCleanupFailure";

    internal static async Task SettleAsync(Process? process, string root, string source,
        Exception? activeFailure, CancellationToken cancellationToken)
    {
        try
        {
            await CleanupAsync(process, root, source, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        if (process is not null)
        {
            if (!process.HasExited)
            {
                Observe(() => process.Kill(entireProcessTree: true), failures);
                await ObserveAsync(() => process.WaitForExitAsync(cancellationToken), failures);
            }
            Observe(process.Dispose, failures);
        }
        if (Directory.Exists(source))
        {
            await ObserveAsync(() => KilledProcessFileReadiness.WaitAsync(source, cancellationToken), failures);
            Observe(() => EpochUpgradeFileInventory.AssertNativeHandlesReleased(source), failures);
        }
        if (Directory.Exists(root))
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

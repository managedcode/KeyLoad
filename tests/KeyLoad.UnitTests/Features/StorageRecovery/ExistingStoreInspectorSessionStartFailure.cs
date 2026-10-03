using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class ExistingStoreInspectorSessionStartFailure
{
    internal static async Task CleanupAsync(Process process, Exception original, Task? exit,
        Task? input, Task? stdout, Task? stderr, Task? completion)
    {
        var failures = new List<Exception> { original };
        var started = true;
        ExistingStoreInspectorFailureJoin.Capture(() => started = HasStarted(process), failures);
        if (started)
        {
            ExistingStoreInspectorFailureJoin.Capture(() => Kill(process), failures);
            ExistingStoreInspectorFailureJoin.Capture(() => CloseInput(process), failures);
            var exited = await ObserveProcessExitAsync(process, exit, failures);
            await ObserveOptionalAsync(input, failures);
            await ObserveOptionalAsync(stdout, failures);
            await ObserveOptionalAsync(stderr, failures);
            await ObserveOptionalAsync(completion, failures);
            if (!exited)
            {
                await RetainUnsettledAsync(process, failures);
            }
        }
        ExistingStoreInspectorFailureJoin.Capture(process.Dispose, failures);
        ExistingStoreInspectorFailureJoin.Throw(failures);
    }

    internal static async Task<bool> ObserveProcessExitAsync(Process process, Task? exit, List<Exception> failures)
    {
        if (exit is null)
        {
            ExistingStoreInspectorFailureJoin.Capture(() => exit = process.WaitForExitAsync(), failures);
        }
        if (exit is not null && await ExistingStoreInspectorFailureJoin.ObserveAsync(exit, failures))
        {
            return true;
        }
        var exited = false;
        ExistingStoreInspectorFailureJoin.Capture(() => { process.WaitForExit(); exited = true; }, failures);
        return exited;
    }

    internal static async Task RetainUnsettledAsync(Process process, List<Exception> failures)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System);
        GC.KeepAlive(process);
        GC.KeepAlive(failures);
    }

    private static async Task ObserveOptionalAsync(Task? task, List<Exception> failures)
    {
        if (task is not null)
        {
            await ExistingStoreInspectorFailureJoin.ObserveAsync(task, failures);
        }
    }

    private static bool HasStarted(Process process)
    {
        try
        {
            return process.Id > 0;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void CloseInput(Process process) => process.StandardInput.Close();

    private static void Kill(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }
}

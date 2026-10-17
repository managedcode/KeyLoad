using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeStoreRuntimeDisposal
{
    private const string OriginalCloseUnjoined = "The original native store close has not returned; retain its root.";
    internal static void Run(ZoneTreeStoreRuntime runtime, bool guarded)
    {
        runtime.NativeReadCuts.CloseAndJoin();
        if (guarded)
        {
            DisposeExisting(runtime);
            return;
        }
        if (!runtime.CacheLifecycle.TryBeginClose(out var control))
        {
            throw new InvalidOperationException(OriginalCloseUnjoined);
        }

        var failures = new List<Exception>();
        ZoneTreePointCacheCleanup.Capture(() =>
        {
            if (control is not null)
            {
                control.CloseAdmission();
            }
            else
            {
                runtime.EmbeddedPointCache?.Disable();
            }
        }, failures);
        runtime.Gate.EnterWriteLock();
        try
        {
            runtime.MarkDisposed();
            ZoneTreePointCacheCleanup.Capture(() => ZoneTreeStoreHandleDisposal.Normal(runtime), failures);
            ZoneTreePointCacheCleanup.Capture(runtime.CacheLifecycle.DisposeCacheUnderWrite, failures);
        }
        finally
        {
            ZoneTreePointCacheCleanup.Capture(runtime.Gate.ExitWriteLock, failures);
            ZoneTreePointCacheCleanup.Capture(runtime.Gate.Dispose, failures);
        }
        ZoneTreePointCacheCleanup.ThrowFailures(failures);
    }

    private static void DisposeExisting(ZoneTreeStoreRuntime runtime)
    {
        if (!runtime.CacheLifecycle.TryBeginClose(out var control))
        {
            throw new InvalidOperationException(OriginalCloseUnjoined);
        }
        var failures = new List<Exception>();
        ZoneTreeExistingStoreCleanup.Capture(() => control?.CloseAdmission(), failures);
        var entered = false;
        ZoneTreeExistingStoreCleanup.Capture(() =>
        {
            runtime.Gate.EnterWriteLock();
            entered = true;
            runtime.MarkDisposed();
        }, failures);
        ZoneTreeExistingStoreCleanup.CloseRegistered(runtime, failures);
        ZoneTreeExistingStoreCleanup.Capture(runtime.CacheLifecycle.DisposeCacheUnderWrite, failures);
        if (entered)
        {
            ZoneTreeExistingStoreCleanup.Capture(runtime.Gate.ExitWriteLock, failures);
        }
        ZoneTreeExistingStoreCleanup.Capture(runtime.Gate.Dispose, failures);
        ZoneTreeExistingStoreCleanup.ThrowFailures(failures);
    }
}

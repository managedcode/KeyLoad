namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>Acquires the actual writer without a wait queue or recursive upgrade.</summary>
internal static class ZoneTreePointCacheStoreGate
{
    private const string DisposalInOperation = "A store cannot be disposed from within its own operation.";

    internal static bool CanBeginDisposal(ZoneTreeStoreRuntime runtime, ZoneTreePointCacheLifecycle lifecycle)
    {
        if (lifecycle.IsClosing)
        {
            return false;
        }

        try
        {
            var gate = runtime.Gate;
            if (gate.IsReadLockHeld || gate.IsWriteLockHeld || gate.IsUpgradeableReadLockHeld)
            {
                throw new LockRecursionException(DisposalInOperation);
            }

            return true;
        }
        catch (ObjectDisposedException) when (lifecycle.IsClosing)
        {
            return false;
        }
    }

    internal static bool TryEnterWrite(ZoneTreeStoreRuntime runtime, ZoneTreePointCacheLifecycle lifecycle,
        out ZoneTreePointCacheControlResult failure)
    {
        failure = ZoneTreePointCacheControlResult.Closed;
        if (lifecycle.IsClosing)
        {
            return false;
        }

        try
        {
            failure = ZoneTreePointCacheControlResult.Busy;
            var gate = runtime.Gate;
            return !gate.IsReadLockHeld && !gate.IsWriteLockHeld && !gate.IsUpgradeableReadLockHeld
                && gate.TryEnterWriteLock(0);
        }
        catch (ObjectDisposedException) when (lifecycle.IsClosing)
        {
            failure = ZoneTreePointCacheControlResult.Closed;
            return false;
        }
    }
}

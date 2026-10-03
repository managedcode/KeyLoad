namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>Acquires actual storage gates without a wait queue or recursive entry.</summary>
internal static class ZoneTreePointCacheStoreGate
{
    private const int NoWaitMilliseconds = 0;
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
                && gate.TryEnterWriteLock(NoWaitMilliseconds);
        }
        catch (ObjectDisposedException) when (lifecycle.IsClosing)
        {
            failure = ZoneTreePointCacheControlResult.Closed;
            return false;
        }
    }

    internal static bool TryEnterRead(ZoneTreeStoreRuntime runtime, ZoneTreePointCacheLifecycle lifecycle,
        out ZoneTreePointCacheOwnerStatus failure)
    {
        failure = ZoneTreePointCacheOwnerStatus.Closed;
        if (lifecycle.IsClosing)
        {
            return false;
        }

        try
        {
            failure = ZoneTreePointCacheOwnerStatus.Busy;
            var gate = runtime.Gate;
            return !gate.IsReadLockHeld && !gate.IsWriteLockHeld && !gate.IsUpgradeableReadLockHeld
                && gate.TryEnterReadLock(NoWaitMilliseconds);
        }
        catch (ObjectDisposedException) when (lifecycle.IsClosing)
        {
            failure = ZoneTreePointCacheOwnerStatus.Closed;
            return false;
        }
    }
}

namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>Projects nonsecret actual ownership under the native reader without consulting cache state locks.</summary>
internal static class ZoneTreePointCacheOwnerObservation
{
    internal static ZoneTreePointCacheOwnerStatus TryRead(ZoneTreeStoreRuntime runtime,
        ZoneTreePointCacheControlState state, Guid runtimeId, out ZoneTreePointCacheOwnerIdentity identity)
    {
        identity = default;
        var lifecycle = runtime.CacheLifecycle;
        if (state.IsClosed || lifecycle.IsClosing)
        {
            return ZoneTreePointCacheOwnerStatus.Closed;
        }
        if (!ZoneTreePointCacheStoreGate.TryEnterRead(runtime, lifecycle, out var failure))
        {
            return failure;
        }

        try
        {
            if (state.IsClosed || lifecycle.IsClosing)
            {
                return ZoneTreePointCacheOwnerStatus.Closed;
            }
            runtime.Check();
            var actual = runtime.Identity;
            var observed = new ZoneTreePointCacheOwnerIdentity(actual.NodeId, actual.Incarnation, runtimeId);
            if (state.IsClosed || lifecycle.IsClosing)
            {
                return ZoneTreePointCacheOwnerStatus.Closed;
            }

            identity = observed;
            return ZoneTreePointCacheOwnerStatus.Healthy;
        }
        finally
        {
            runtime.Gate.ExitReadLock();
        }
    }
}

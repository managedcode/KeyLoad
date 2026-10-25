using KeyLoad.Storage.ZoneTree.ResourceExecution;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeStorePointCacheOpening
{
    internal static global::ManagedCode.Communication.Result<ZoneTreePointCache?> Open(ZoneTreeStoreRuntime runtime)
    {
        if (runtime.Options.EmbeddedPointCache is not { } options)
        {
            return global::ManagedCode.Communication.Result<ZoneTreePointCache?>.FailNotFound(nameof(Open));
        }
        runtime.OwnershipSettlement.BeginConstructor();
        var cache = ZoneTreePointCache.CreateResolved(options);
        runtime.OwnershipSettlement.ConstructorReturned();
        return (cache);
    }
}

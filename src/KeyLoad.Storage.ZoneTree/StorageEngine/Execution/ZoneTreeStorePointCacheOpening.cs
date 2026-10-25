using KeyLoad.Storage.ZoneTree.ResourceExecution;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeStorePointCacheOpening
{
    internal static (bool HasValue, ZoneTreePointCache? Value) Open(ZoneTreeStoreRuntime runtime)
    {
        if (runtime.Options.EmbeddedPointCache is not { } options)
        {
            return (false, default(ZoneTreePointCache?));
        }
        runtime.OwnershipSettlement.BeginConstructor();
        var cache = ZoneTreePointCache.CreateResolved(options);
        runtime.OwnershipSettlement.ConstructorReturned();
        return (true, (cache));
    }
}

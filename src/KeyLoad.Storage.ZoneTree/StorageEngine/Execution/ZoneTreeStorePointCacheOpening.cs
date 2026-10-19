using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeStorePointCacheOpening
{
    internal static ZoneTreePointCache? Open(ZoneTreeStoreRuntime runtime)
    {
        if (runtime.Options.EmbeddedPointCache is not { } options)
        {
            return null;
        }
        runtime.OwnershipSettlement.BeginConstructor();
        var cache = ZoneTreePointCache.CreateResolved(options);
        runtime.OwnershipSettlement.ConstructorReturned();
        return cache;
    }
}

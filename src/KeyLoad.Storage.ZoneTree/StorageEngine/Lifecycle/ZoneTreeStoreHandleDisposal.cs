namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeStoreHandleDisposal
{
    internal static void Close(ZoneTreeStoreRuntime runtime)
    {
        var failures = new List<Exception>();
        ZoneTreeExistingStoreCleanup.Capture(() => RetireMaintainer(runtime), failures);
        ZoneTreeExistingStoreCleanup.Capture(() => runtime.PlainMaintainer?.Dispose(), failures);
        ZoneTreeExistingStoreCleanup.Capture(() => runtime.PlainTree?.Dispose(), failures);
        ZoneTreeExistingStoreCleanup.Capture(() => RetireTree(runtime), failures);
        ZoneTreeExistingStoreCleanup.Capture(() => runtime.Ownership?.Dispose(), failures);
        ZoneTreeExistingStoreCleanup.ThrowFailures(failures);
    }

    internal static void RetireMaintainer(ZoneTreeStoreRuntime runtime)
    {
        if (runtime.Maintainer is not { } maintenance)
        {
            return;
        }

        maintenance.Dispose();
        runtime.Maintainer = null;
    }

    internal static void RetireTree(ZoneTreeStoreRuntime runtime)
    {
        if (runtime.Tree is not { } tree)
        {
            return;
        }

        tree.Dispose();
        runtime.Tree = null!;
    }

}

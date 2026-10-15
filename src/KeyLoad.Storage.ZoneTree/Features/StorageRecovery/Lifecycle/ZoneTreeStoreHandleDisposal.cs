namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeStoreHandleDisposal
{
    internal static void FailedOpen(ZoneTreeStoreRuntime runtime)
        => Close(runtime, failedOpen: true);

    internal static void Normal(ZoneTreeStoreRuntime runtime)
        => Close(runtime, failedOpen: false);

    private static void Close(ZoneTreeStoreRuntime runtime, bool failedOpen)
    {
        var failures = new List<Exception>();
        ZoneTreeExistingStoreCleanup.Capture(() => RetireMaintainer(runtime), failures);
        if (failedOpen)
        {
            ZoneTreeExistingStoreCleanup.Capture(() => runtime.Journal?.Dispose(), failures);
        }
        ZoneTreeExistingStoreCleanup.Capture(() => RetireTree(runtime), failures);
        if (!failedOpen)
        {
            ZoneTreeExistingStoreCleanup.Capture(() => runtime.Journal?.Dispose(), failures);
        }
        ZoneTreeExistingStoreCleanup.Capture(() => runtime.Ownership?.Dispose(), failures);
        ZoneTreeExistingStoreCleanup.ThrowFailures(failures);
    }

    internal static void RetireMaintainer(ZoneTreeStoreRuntime runtime)
    {
        if (runtime.Maintenance is not { } maintenance)
        {
            return;
        }

        maintenance.Dispose();
        runtime.Maintenance = null;
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

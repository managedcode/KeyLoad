namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeStoreConstructionCleanup
{
    private const int FirstFailureIndex = 0;
    private const int EmptyFailureCount = 0;
    internal static void Run(ZoneTreeStoreRuntime runtime, Exception original, bool guarded)
    {
        var failures = new List<Exception>();
        if (guarded)
        {
            ZoneTreeExistingStoreCleanup.CloseRegistered(runtime, failures);
        }
        else
        {
            ZoneTreeExistingStoreCleanup.Capture(() => ZoneTreeStoreHandleDisposal.FailedOpen(runtime), failures);
        }
        ZoneTreeExistingStoreCleanup.Capture(runtime.Gate.Dispose, failures);
        if (failures.Count == EmptyFailureCount)
        {
            runtime.OwnershipSettlement.ReleaseReturned();
        }
        failures.Insert(FirstFailureIndex, original);
        ZoneTreeExistingStoreCleanup.ThrowFailures(failures);
    }
}

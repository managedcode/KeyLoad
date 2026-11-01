namespace KeyLoad.Storage.ZoneTree;

/// <summary>Closes native maintenance and trees after the owning session has joined its operations and retained reads.</summary>
internal static class ZoneTreeStoreRuntimeDisposal
{
    internal static void Run(ZoneTreeStoreRuntime runtime, bool guarded)
    {
        runtime.RuntimeReadCuts.CloseAndJoin();
        runtime.MarkDisposed();
        var failures = new List<Exception>();
        if (guarded)
        {
            ZoneTreeExistingStoreCleanup.CloseRegistered(runtime, failures);
        }
        else
        {
            ZoneTreeExistingStoreCleanup.Capture(() => ZoneTreeStoreHandleDisposal.Close(runtime), failures);
        }
        ZoneTreeExistingStoreCleanup.ThrowFailures(failures);
    }
}

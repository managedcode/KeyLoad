namespace KeyLoad.Server.Search;

internal static class AnnGenerationShutdown
{
    internal static async Task CompleteAsync(Task originalReaders, Lock gate, AnnMaintenanceMemory MaintenanceMemory,
        List<AnnGenerationSlot> slots, AnnRootLease root, List<Exception> failures)
    {
        await ServerFailureObserver.ObserveAsync(() => originalReaders, failures);
        lock (gate)
        {
            MaintenanceMemory.Close();
            slots.Clear();
            ServerFailureObserver.Observe(root.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

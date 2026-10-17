namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnGenerationShutdown
{
    internal static async Task CompleteAsync(Task originalReaders, Lock gate, NativeAnnMaintenanceMemory MaintenanceMemory,
        List<NativeAnnGenerationSlot> slots, NativeAnnRootLease root, List<Exception> failures)
    {
        await ServerFailureObserver.ObserveAsync(() => originalReaders, failures).ConfigureAwait(false);
        lock (gate)
        {
            MaintenanceMemory.Close();
            slots.Clear();
            ServerFailureObserver.Observe(root.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnOwnerAdmission
{
    internal static Task<T> RunAsync<T>(NativeAnnGenerationOwner owner, Lock gate, ref bool closed,
        NativeAnnWorker worker, NativeAnnStageStore stages, Func<NativeAnnStageStore, T> operation)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, owner);
            return Task.FromResult(worker.Run(() => operation(stages)));
        }
    }

    internal static NativeAnnReadReservation ReserveRead(NativeAnnGenerationOwner owner, Lock gate, ref bool closed,
        List<NativeAnnGenerationSlot> slots, NativeAnnExecutionOptions options, NativeAnnReadReservations publicReads, NativeAnnMaintenanceMemory MaintenanceMemory, long desiredBytes)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, owner);
            return NativeAnnPublicReadAdmission.Reserve(slots, options, publicReads, desiredBytes, MaintenanceMemory.Bytes);
        }
    }

    internal static NativeAnnManifest DescribePublished(NativeAnnGenerationOwner owner, Lock gate, ref bool closed,
        List<NativeAnnGenerationSlot> slots, NativeAnnOwnedKey key)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, owner);
            return NativeAnnPublicReadAdmission.Describe(slots, key);
        }
    }

    internal static void ReleaseGeneration(NativeAnnGenerationOwner owner, Lock gate, ref bool closed,
        NativeAnnStageStore stages, List<NativeAnnGenerationSlot> slots, NativeAnnExecutionOptions options, NativeAnnRootLease root, NativeAnnOwnedKey key)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, owner);
            stages.Release(key);
            foreach (var slot in slots.Where(item => NativeAnnSlotRetention.SameKey(item.Manifest, key)))
            {
                slot.Retire();
            }
            NativeAnnSlotRetention.RetireUnpinned(root, slots, options);
        }
    }
}

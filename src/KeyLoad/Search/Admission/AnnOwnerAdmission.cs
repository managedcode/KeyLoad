namespace KeyLoad.Server.Features.Search;

internal static class AnnOwnerAdmission
{
    internal static Task<T> RunAsync<T>(AnnGenerationOwner owner, Lock gate, ref bool closed,
        AnnWorker worker, AnnStageStore stages, Func<AnnStageStore, T> operation)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, owner);
            return Task.FromResult(worker.Run(() => operation(stages)));
        }
    }

    internal static AnnReadReservation ReserveRead(AnnGenerationOwner owner, Lock gate, ref bool closed,
        List<AnnGenerationSlot> slots, AnnExecutionOptions options, AnnReadReservations publicReads, AnnMaintenanceMemory MaintenanceMemory, long desiredBytes)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, owner);
            return AnnPublicReadAdmission.Reserve(slots, options, publicReads, desiredBytes, MaintenanceMemory.Bytes);
        }
    }

    internal static AnnManifest DescribePublished(AnnGenerationOwner owner, Lock gate, ref bool closed,
        List<AnnGenerationSlot> slots, AnnOwnedKey key)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, owner);
            return AnnPublicReadAdmission.Describe(slots, key);
        }
    }

    internal static void ReleaseGeneration(AnnGenerationOwner owner, Lock gate, ref bool closed,
        AnnStageStore stages, List<AnnGenerationSlot> slots, AnnExecutionOptions options, AnnRootLease root, AnnOwnedKey key)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, owner);
            stages.Release(key);
            foreach (var slot in slots.Where(item => AnnSlotRetention.SameKey(item.Manifest, key)))
            {
                slot.Retire();
            }
            AnnSlotRetention.RetireUnpinned(root, slots, options);
        }
    }
}

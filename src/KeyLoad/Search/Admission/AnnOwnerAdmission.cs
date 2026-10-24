namespace KeyLoad.Server.Search;

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
        EnterPublicRead(gate);
        try
        {
            ObjectDisposedException.ThrowIf(closed, owner);
            return AnnPublicReadAdmission.Reserve(slots, options, publicReads, desiredBytes, MaintenanceMemory.Bytes);
        }
        finally { gate.Exit(); }
    }

    internal static AnnManifest DescribePublished(AnnGenerationOwner owner, Lock gate, ref bool closed,
        List<AnnGenerationSlot> slots, AnnOwnedKey key)
    {
        EnterPublicRead(gate);
        try
        {
            ObjectDisposedException.ThrowIf(closed, owner);
            return AnnPublicReadAdmission.Describe(slots, key);
        }
        finally { gate.Exit(); }
    }

    internal static void EnterPublicRead(Lock gate)
    {
        if (!gate.TryEnter())
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, AnnProtocol.Bound);
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

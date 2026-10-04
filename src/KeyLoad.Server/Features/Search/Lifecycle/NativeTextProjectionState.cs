using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextProjectionState
{
    private readonly object sync = new();
    private readonly NativeTextProjectionWork work = new();
    private readonly NativeTextProjectionSlotSet slots = new();
    private readonly NativeTextProjectionInvalidation invalidation;
    private readonly NativeTextProjectionShutdownState shutdown;

    internal NativeTextProjectionState()
    {
        invalidation = new(sync, slots);
        shutdown = new(sync, work, slots);
    }

    internal NativeTextProjectionReservation Reserve(TextProjectionScope scope)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(shutdown.Closing, typeof(NativeTextProjection));
            if (slots.Transition || slots.CleanupActive || slots.ReplacementWaitingForRetiredLease)
            {
                throw NativeTextErrors.Busy();
            }
            var existing = slots.FindCurrent(scope);
            if (existing is not null)
            {
                ReserveLease(existing);
                return new(existing, false);
            }
            if (slots.Building || work.ActiveLeases >= 2 || slots.HasUnleasedRetired
                || slots.Count >= NativeTextPhysicalBudget.MaximumGenerations)
            {
                throw NativeTextErrors.Busy();
            }
            var staged = slots.Reserve();
            ReserveLease(staged);
            return new(staged, true);
        }
    }

    internal void RecordLeaf(NativeTextGenerationSlot slot, string leaf)
    {
        lock (sync)
        {
            slots.RecordLeaf(slot, leaf);
        }
    }

    internal void MarkOwnerCreated(NativeTextGenerationSlot slot)
    {
        lock (sync)
        {
            slot.PhysicalOwnerCreated = true;
        }
    }

    internal void AttachGeneration(NativeTextGenerationSlot slot, NativeTextGeneration generation)
    {
        lock (sync)
        {
            slots.AttachGeneration(slot, generation);
        }
    }

    internal NativeTextGenerationSlot? FailAcquire(NativeTextProjectionReservation reservation, bool preserveOwner)
    {
        lock (sync)
        {
            reservation.Slot.LeaseActive = false;
            work.EndLease();
            if (preserveOwner)
            {
                reservation.Slot.Unsettled = true;
            }
            else
            {
                slots.ClearStaged(reservation.Slot);
            }
            var cleanup = preserveOwner ? null : ReserveRetiredCleanup();
            return cleanup;
        }
    }

    internal NativeTextPublishPlan Publish(NativeTextGeneration generation)
    {
        lock (sync)
        {
            return slots.Publish(generation);
        }
    }

    internal void CompleteReplacement(NativeTextGenerationSlot previous,
        NativeTextGeneration generation)
    {
        lock (sync)
        {
            slots.CompleteReplacement(previous, generation);
        }
    }

    internal void CancelReplacement()
    {
        lock (sync)
        {
            slots.CancelReplacement();
        }
    }

    internal bool BeginInvalidation(NativeTextGeneration generation) => invalidation.Begin(generation);

    internal void ClearCurrent(NativeTextGeneration generation) => invalidation.ClearCurrent(generation);

    internal void ClearFailedBuild(NativeTextGenerationSlot slot) => invalidation.ClearFailedBuild(slot);

    internal void CaptureSlots(out NativeTextGenerationSlot? first, out NativeTextGenerationSlot? second,
        out NativeTextGenerationSlot? third) => shutdown.CaptureSlots(out first, out second, out third);

    internal NativeTextGenerationSlot? CompleteLease(NativeTextGenerationSlot slot, Exception? failure)
    {
        lock (sync)
        {
            if (!slot.LeaseActive)
            {
                throw NativeTextErrors.Corrupt();
            }
            slot.LeaseActive = false;
            work.EndLease();
            if (failure is not null)
            {
                slot.Unsettled = true;
            }
            slots.CompleteLease(slot, failure);
            var cleanup = failure is null && work.ActiveLeases == 0 ? ReserveRetiredCleanup() : null;
            return cleanup;
        }
    }

    internal void CompleteRetirement(NativeTextGenerationSlot slot, Exception? failure)
    {
        lock (sync)
        {
            slots.CompleteRetirement(slot, failure);
            work.EndCleanup();
        }
    }

    internal void BeginShutdown() => shutdown.Begin();

    internal void WaitForQuiet() => shutdown.WaitForQuiet();

    internal NativeTextGenerationSlot?[] SnapshotSlots() => shutdown.SnapshotSlots();

    internal void CompleteShutdown(NativeTextGenerationSlot slot, Exception? failure)
        => shutdown.Complete(slot, failure);

    private void ReserveLease(NativeTextGenerationSlot slot)
    {
        if (slot.LeaseActive || slot.Unsettled || work.ActiveLeases >= 2)
        {
            throw slot.Unsettled ? NativeTextErrors.Corrupt() : NativeTextErrors.Busy();
        }
        slot.LeaseActive = true;
        work.StartLease();
    }

    private NativeTextGenerationSlot? ReserveRetiredCleanup()
    {
        if (work.ActiveLeases != 0 || slots.RetiredCandidate is not { } retired
            || !slots.BeginRetirement(retired))
        {
            return null;
        }
        work.StartCleanup();
        return retired;
    }
}

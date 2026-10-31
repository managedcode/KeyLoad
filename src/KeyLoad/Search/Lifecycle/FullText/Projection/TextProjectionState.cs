using KeyLoad.Query.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal sealed class TextProjectionState
{
    private readonly IOptions<TextExecutionOptions> executionOptions;

    private readonly Lock sync = new();
    private readonly TextProjectionWork work = new();
    private readonly TextProjectionSlotSet slots = new();
    private readonly TextProjectionInvalidation invalidation;
    private readonly TextProjectionShutdownState shutdown;

    internal TextProjectionState(IOptions<TextExecutionOptions> executionOptions)
    {
        this.executionOptions = executionOptions;
        invalidation = new(sync, slots);
        shutdown = new(sync, work, slots);
    }

    internal TextProjectionReservation Reserve(TextProjectionScope scope)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(shutdown.Closing, typeof(RuntimeTextProjection));
            var existing = (slots.FindCurrent(scope)).Value;
            if (existing is not null && !slots.Transition)
            {
                ReserveLease(existing);
                return new(existing, false);
            }
            if (slots.Transition || slots.CleanupActive || slots.ReplacementWaitingForRetiredLease
                || slots.Building || slots.HasUnleasedRetired
                || slots.Count >= executionOptions.Value.MaximumGenerations)
            {
                throw TextErrors.Busy();
            }
            var staged = slots.Reserve();
            ReserveLease(staged);
            return new(staged, true);
        }
    }

    internal void RecordLeaf(TextGenerationSlot slot, string leaf)
    {
        lock (sync)
        {
            slots.RecordLeaf(slot, leaf);
        }
    }

    internal void MarkOwnerCreated(TextGenerationSlot slot)
    {
        lock (sync)
        {
            slot.PhysicalOwnerCreated = true;
        }
    }

    internal void AttachGeneration(TextGenerationSlot slot, TextGeneration generation)
    {
        lock (sync)
        {
            slots.AttachGeneration(slot, generation);
        }
    }

    internal TextGenerationSlot? FailAcquire(TextProjectionReservation reservation, bool preserveOwner)
    {
        lock (sync)
        {
            reservation.Slot.Readers--;
            work.EndLease();
            if (preserveOwner)
            {
                reservation.Slot.Unsettled = true;
            }
            else
            {
                slots.ClearStaged(reservation.Slot);
            }
            var cleanup = preserveOwner ? null : (ReserveRetiredCleanup()).Value;
            return cleanup;
        }
    }

    internal TextPublishPlan Publish(TextGeneration generation)
    {
        lock (sync)
        {
            return slots.Publish(generation);
        }
    }

    internal void CompleteReplacement(TextGenerationSlot previous,
        TextGeneration generation)
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

    internal bool BeginInvalidation(TextGeneration generation) => invalidation.Begin(generation);

    internal void ClearCurrent(TextGeneration generation) => invalidation.ClearCurrent(generation);

    internal void ClearFailedBuild(TextGenerationSlot slot) => invalidation.ClearFailedBuild(slot);

    internal void CaptureSlots(out TextGenerationSlot? first, out TextGenerationSlot? second,
        out TextGenerationSlot? third) => shutdown.CaptureSlots(out first, out second, out third);

    internal bool BeginRelease(TextGenerationSlot slot, bool invalidated, out bool invalidate)
    {
        lock (sync)
        {
            if (slot.Readers <= 0)
            {
                throw TextErrors.Corrupt();
            }
            slot.Readers--;
            slot.SettlingReaders++;
            slot.Invalidated |= invalidated;
            invalidate = slot.Invalidated;
            return slot.Readers == 0 && (invalidate || slot.Generation is { Published: false });
        }
    }

    internal TextGenerationSlot? CompleteLease(TextGenerationSlot slot, Exception? failure)
    {
        const int EmptyActiveLeases = 0;

        lock (sync)
        {
            if (slot.SettlingReaders <= 0)
            {
                throw TextErrors.Corrupt();
            }
            slot.SettlingReaders--;
            work.EndLease();
            if (failure is not null)
            {
                slot.Unsettled = true;
            }
            slots.CompleteLease(slot, failure);
            var cleanup = failure is null && work.ActiveLeases == EmptyActiveLeases ? (ReserveRetiredCleanup()).Value : null;
            return cleanup;
        }
    }

    internal void CompleteRetirement(TextGenerationSlot slot, Exception? failure)
    {
        lock (sync)
        {
            slots.CompleteRetirement(slot, failure);
            work.EndCleanup();
        }
    }

    internal void BeginShutdown() => shutdown.Begin();

    internal void WaitForQuiet() => shutdown.WaitForQuiet();

    internal TextGenerationSlot?[] SnapshotSlots() => shutdown.SnapshotSlots();

    internal void CompleteShutdown(TextGenerationSlot slot, Exception? failure)
        => shutdown.Complete(slot, failure);

    private void ReserveLease(TextGenerationSlot slot)
    {
        if (slot.CleanupActive || slot.Unsettled || slot.Invalidated)
        {
            throw slot.Unsettled || slot.Invalidated ? TextErrors.Corrupt() : TextErrors.Busy();
        }
        slot.Readers++;
        work.StartLease();
    }

    private (bool HasValue, TextGenerationSlot? Value) ReserveRetiredCleanup()
    {
        const int EmptyActiveLeases = 0;

        if (work.ActiveLeases != EmptyActiveLeases || slots.RetiredCandidate.Value is not { } retired
            || !slots.BeginRetirement(retired))
        {
            return (false, default(TextGenerationSlot?));
        }
        work.StartCleanup();
        return (true, ((TextGenerationSlot?)(retired)));
    }
}

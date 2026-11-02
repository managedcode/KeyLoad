using KeyLoad.Query.Search;

namespace KeyLoad.Server.Search;

internal sealed class TextProjectionSlotSet
{
    private TextGenerationSlot? current;
    private TextGenerationSlot? staged;
    private TextGenerationSlot? retired;
    private bool transition;

    internal bool Building => staged is not null;
    internal TextGenerationSlot? Current => current;
    internal bool Transition => transition;
    internal bool ReplacementWaitingForRetiredLease => retired?.LeaseActive == true
        && (staged is not null || current?.LeaseActive == true);
    internal bool CleanupActive => current?.CleanupActive == true || retired?.CleanupActive == true
        || staged?.CleanupActive == true;
    internal bool HasUnleasedRetired => retired is { LeaseActive: false };
    internal int Count => (current is null ? 0 : 1) + (staged is null ? 0 : 1) + (retired is null ? 0 : 1);
    internal (bool HasValue, TextGenerationSlot? Value) RetiredCandidate
        => retired is { LeaseActive: false } && staged is null && !transition
            ? (true, retired)
            : (false, default(TextGenerationSlot?));

    internal (bool HasValue, TextGenerationSlot? Value) FindCurrent(TextProjectionScope scope)
        => current?.Generation is { } generation && generation.Scope == scope ? (true, ((TextGenerationSlot?)(current))) : (false, default(TextGenerationSlot?));

    internal TextGenerationSlot Reserve()
    {
        if (staged is not null || transition || retired is { LeaseActive: false })
        {
            throw TextErrors.Busy();
        }
        staged = new(null);
        return staged;
    }

    internal void RecordLeaf(TextGenerationSlot slot, string leaf)
    {
        RequireStaged(slot);
        slot.Leaf = leaf;
    }

    internal void AttachGeneration(TextGenerationSlot slot, TextGeneration generation)
    {
        RequireStaged(slot);
        slot.Generation = generation;
    }

    internal void ClearStaged(TextGenerationSlot slot)
    {
        if ((ReferenceEquals(staged, slot) /* KLD0041: Clear only the supplied staged slot instance through CLR reference identity. */))
        {
            staged = null;
        }
    }

    internal TextPublishPlan Publish(TextGeneration generation, Task availability)
    {
        var slot = staged;
        if (slot?.Generation != generation || slot.Unsettled || transition)
        {
            throw TextErrors.Corrupt();
        }
        if (retired is not null)
        {
            if (!retired.LeaseActive)
            {
                throw TextProjectionAdmission.Busy(TextProtocol.ProjectionBusy, availability);
            }
            if (current is null)
            {
                current = slot;
                staged = null;
                return new(null, false);
            }
            if (current.LeaseActive)
            {
                throw TextProjectionAdmission.Busy(TextProtocol.ProjectionBusy, availability);
            }
            transition = true;
            return new(current, true);
        }
        var previous = current;
        current = slot;
        staged = null;
        retired = previous;
        return new(previous, false);
    }

    internal void CompleteReplacement(TextGenerationSlot previous, TextGeneration generation)
    {
        var slot = staged;
        if (!transition || !(ReferenceEquals(current, previous) /* KLD0041: Require the expected current generation slot instance during publication through CLR reference identity. */) || slot?.Generation != generation || retired is null)
        {
            throw TextErrors.Corrupt();
        }
        current = slot;
        staged = null;
        transition = false;
    }

    internal void CancelReplacement()
    {
        if (!transition)
        {
            return;
        }
        staged?.Unsettled = true;
        current?.Unsettled = true;
        transition = false;
    }

    internal void ClearCurrent(TextGeneration generation)
    {
        if ((ReferenceEquals(current?.Generation, generation) /* KLD0041: Clear only the supplied current generation instance through CLR reference identity. */))
        {
            current = null;
        }
    }

    internal void CompleteLease(TextGenerationSlot slot, Exception? failure)
    {
        if (failure is null && (ReferenceEquals(staged, slot) /* KLD0041: Complete only the staged slot instance that owns this lease through CLR reference identity. */)
            && (slot.Generation is null or { Published: false }))
        {
            staged = null;
        }
    }

    internal bool BeginRetirement(TextGenerationSlot slot)
    {
        if (!(ReferenceEquals(retired, slot) /* KLD0041: Retire only the supplied retired slot instance through CLR reference identity. */) || slot.LeaseActive || slot.CleanupActive || transition)
        {
            return false;
        }
        slot.CleanupActive = true;
        return true;
    }

    internal void CompleteRetirement(TextGenerationSlot slot, Exception? failure)
    {
        slot.CleanupActive = false;
        if (failure is null && (ReferenceEquals(retired, slot) /* KLD0041: Clear only the retired slot instance whose cleanup completed through CLR reference identity. */))
        {
            retired = null;
        }
        else if (failure is not null)
        {
            slot.Unsettled = true;
        }
    }

    internal TextGenerationSlot?[] Snapshot() => [current, retired, staged];

    internal void CaptureSlots(out TextGenerationSlot? first, out TextGenerationSlot? second,
        out TextGenerationSlot? third)
    {
        first = current;
        second = retired;
        third = staged;
    }

    internal void Remove(TextGenerationSlot slot)
    {
        if ((ReferenceEquals(current, slot) /* KLD0041: Remove only this current slot instance through CLR reference identity. */))
        {
            current = null;
        }
        if ((ReferenceEquals(retired, slot) /* KLD0041: Remove only this retired slot instance through CLR reference identity. */))
        {
            retired = null;
        }
        if ((ReferenceEquals(staged, slot) /* KLD0041: Remove only this staged slot instance through CLR reference identity. */))
        {
            staged = null;
        }
    }

    internal void Capture(out TextGeneration? first, out TextGeneration? second,
        out TextGeneration? third)
    {
        first = current?.Generation;
        second = retired?.Generation;
        third = staged?.Generation;
    }

    private void RequireStaged(TextGenerationSlot slot)
    {
        if (!(ReferenceEquals(staged, slot) /* KLD0041: Require the exact staged slot instance before changing its state through CLR reference identity. */) || slot.Unsettled)
        {
            throw TextErrors.Corrupt();
        }
    }
}

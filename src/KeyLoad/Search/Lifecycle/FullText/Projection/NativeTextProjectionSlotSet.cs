using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextProjectionSlotSet
{
    private const int GetCountAbsentCount = 0;
    private const int GetCountPresentCount = 1;

    private NativeTextGenerationSlot? current;
    private NativeTextGenerationSlot? staged;
    private NativeTextGenerationSlot? retired;
    private bool transition;

    internal bool Building => staged is not null;
    internal NativeTextGenerationSlot? Current => current;
    internal bool Transition => transition;
    internal bool ReplacementWaitingForRetiredLease => staged is not null && retired?.LeaseActive == true;
    internal bool CleanupActive => current?.CleanupActive == true || retired?.CleanupActive == true
        || staged?.CleanupActive == true;
    internal bool HasUnleasedRetired => retired is { LeaseActive: false };
    internal int Count => (current is null ? GetCountAbsentCount : GetCountPresentCount) + (staged is null ? GetCountAbsentCount : GetCountPresentCount) + (retired is null ? GetCountAbsentCount : GetCountPresentCount);
    internal NativeTextGenerationSlot? RetiredCandidate
        => retired is { LeaseActive: false } && staged is null && !transition ? retired : null;

    internal NativeTextGenerationSlot? FindCurrent(TextProjectionScope scope)
        => current?.Generation is { } generation && generation.Scope == scope ? current : null;

    internal NativeTextGenerationSlot Reserve()
    {
        if (staged is not null || transition || retired is { LeaseActive: false })
        {
            throw NativeTextErrors.Busy();
        }
        staged = new(null);
        return staged;
    }

    internal void RecordLeaf(NativeTextGenerationSlot slot, string leaf)
    {
        RequireStaged(slot);
        slot.Leaf = leaf;
    }

    internal void AttachGeneration(NativeTextGenerationSlot slot, NativeTextGeneration generation)
    {
        RequireStaged(slot);
        slot.Generation = generation;
    }

    internal void ClearStaged(NativeTextGenerationSlot slot)
    {
        if (ReferenceEquals(staged, slot))
        {
            staged = null;
        }
    }

    internal NativeTextPublishPlan Publish(NativeTextGeneration generation)
    {
        var slot = staged;
        if (slot?.Generation != generation || slot.Unsettled || transition)
        {
            throw NativeTextErrors.Corrupt();
        }
        if (retired is not null)
        {
            if (!retired.LeaseActive)
            {
                throw NativeTextErrors.Busy();
            }
            if (current is null)
            {
                current = slot;
                staged = null;
                return new(null, false);
            }
            if (current.LeaseActive)
            {
                throw NativeTextErrors.Busy();
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

    internal void CompleteReplacement(NativeTextGenerationSlot previous, NativeTextGeneration generation)
    {
        var slot = staged;
        if (!transition || !ReferenceEquals(current, previous) || slot?.Generation != generation || retired is null)
        {
            throw NativeTextErrors.Corrupt();
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

    internal void ClearCurrent(NativeTextGeneration generation)
    {
        if (ReferenceEquals(current?.Generation, generation))
        {
            current = null;
        }
    }

    internal void CompleteLease(NativeTextGenerationSlot slot, Exception? failure)
    {
        if (failure is null && ReferenceEquals(staged, slot)
            && (slot.Generation is null or { Published: false }))
        {
            staged = null;
        }
    }

    internal bool BeginRetirement(NativeTextGenerationSlot slot)
    {
        if (!ReferenceEquals(retired, slot) || slot.LeaseActive || slot.CleanupActive || transition)
        {
            return false;
        }
        slot.CleanupActive = true;
        return true;
    }

    internal void CompleteRetirement(NativeTextGenerationSlot slot, Exception? failure)
    {
        slot.CleanupActive = false;
        if (failure is null && ReferenceEquals(retired, slot))
        {
            retired = null;
        }
        else if (failure is not null)
        {
            slot.Unsettled = true;
        }
    }

    internal NativeTextGenerationSlot?[] Snapshot() => [current, retired, staged];

    internal void CaptureSlots(out NativeTextGenerationSlot? first, out NativeTextGenerationSlot? second,
        out NativeTextGenerationSlot? third)
    {
        first = current;
        second = retired;
        third = staged;
    }

    internal void Remove(NativeTextGenerationSlot slot)
    {
        if (ReferenceEquals(current, slot))
        {
            current = null;
        }
        if (ReferenceEquals(retired, slot))
        {
            retired = null;
        }
        if (ReferenceEquals(staged, slot))
        {
            staged = null;
        }
    }

    internal void Capture(out NativeTextGeneration? first, out NativeTextGeneration? second,
        out NativeTextGeneration? third)
    {
        first = current?.Generation;
        second = retired?.Generation;
        third = staged?.Generation;
    }

    private void RequireStaged(NativeTextGenerationSlot slot)
    {
        if (!ReferenceEquals(staged, slot) || slot.Unsettled)
        {
            throw NativeTextErrors.Corrupt();
        }
    }
}

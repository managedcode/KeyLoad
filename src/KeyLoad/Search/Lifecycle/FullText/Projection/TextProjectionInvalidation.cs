namespace KeyLoad.Server.Search;

internal sealed class TextProjectionInvalidation(Lock sync, TextProjectionSlotSet slots)
{
    internal bool Begin(TextGeneration generation)
    {
        lock (sync)
        {
            var current = slots.Current;
            if (!(ReferenceEquals(current?.Generation, generation) /* KLD0041: Invalidate only the currently published generation instance through CLR reference identity. */))
            {
                return false;
            }
            current!.Unsettled = true;
            return true;
        }
    }

    internal void ClearCurrent(TextGeneration generation)
    {
        lock (sync)
        {
            slots.ClearCurrent(generation);
        }
    }

    internal void ClearFailedBuild(TextGenerationSlot slot)
    {
        lock (sync)
        {
            slot.Generation = null;
            slot.Leaf = null;
            slot.PhysicalOwnerCreated = false;
        }
    }
}

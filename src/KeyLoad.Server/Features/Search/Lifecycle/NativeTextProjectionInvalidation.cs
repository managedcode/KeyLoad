namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextProjectionInvalidation(object sync, NativeTextProjectionSlotSet slots)
{
    internal bool Begin(NativeTextGeneration generation)
    {
        lock (sync)
        {
            var current = slots.Current;
            if (!ReferenceEquals(current?.Generation, generation))
            {
                return false;
            }
            current!.Unsettled = true;
            return true;
        }
    }

    internal void ClearCurrent(NativeTextGeneration generation)
    {
        lock (sync)
        {
            slots.ClearCurrent(generation);
        }
    }

    internal void ClearFailedBuild(NativeTextGenerationSlot slot)
    {
        lock (sync)
        {
            slot.Generation = null;
            slot.Leaf = null;
            slot.PhysicalOwnerCreated = false;
        }
    }
}

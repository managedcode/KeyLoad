namespace KeyLoad.Server.Search;

internal sealed class TextProjectionShutdownState(Lock sync, TextProjectionWork work,
    TextProjectionSlotSet slots)
{
    internal bool Closing
    {
        get; private set;
    }

    internal void Begin()
    {
        lock (sync)
        {
            Closing = true;
        }
    }

    internal void WaitForQuiet()
    {
        while (true)
        {
            Task wait;
            lock (sync)
            {
                if (work.IsQuiet)
                {
                    return;
                }
                wait = work.Signal;
            }
            wait.GetAwaiter().GetResult();
        }
    }

    internal TextGenerationSlot?[] SnapshotSlots()
    {
        lock (sync)
        {
            return slots.Snapshot();
        }
    }

    internal void Complete(TextGenerationSlot slot, Exception? failure)
    {
        lock (sync)
        {
            if (failure is not null)
            {
                slot.Unsettled = true;
                return;
            }
            slots.Remove(slot);
        }
    }

    internal void CaptureSlots(out TextGenerationSlot? first, out TextGenerationSlot? second,
        out TextGenerationSlot? third)
    {
        lock (sync)
        {
            slots.CaptureSlots(out first, out second, out third);
        }
    }
}

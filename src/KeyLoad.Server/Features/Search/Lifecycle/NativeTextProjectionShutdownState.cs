namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextProjectionShutdownState(Lock sync, NativeTextProjectionWork work,
    NativeTextProjectionSlotSet slots)
{
    internal bool Closing { get; private set; }

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

    internal NativeTextGenerationSlot?[] SnapshotSlots()
    {
        lock (sync)
        {
            return slots.Snapshot();
        }
    }

    internal void Complete(NativeTextGenerationSlot slot, Exception? failure)
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

    internal void CaptureSlots(out NativeTextGenerationSlot? first, out NativeTextGenerationSlot? second,
        out NativeTextGenerationSlot? third)
    {
        lock (sync)
        {
            slots.CaptureSlots(out first, out second, out third);
        }
    }
}

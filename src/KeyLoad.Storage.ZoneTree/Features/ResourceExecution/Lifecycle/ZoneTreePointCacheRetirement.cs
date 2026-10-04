namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>Detaches indexed entries while preserving charges held by active readers.</summary>
internal static class ZoneTreePointCacheRetirement
{
    internal static bool TryRetireVictim(ZoneTreePointCacheState state)
    {
        var victim = state.LeastRecentlyUsed?.Last?.Value;
        if (victim is null)
        {
            return false;
        }

        Retire(state, victim, eviction: true);
        return true;
    }

    internal static void Retire(ZoneTreePointCacheState state, ZoneTreePointCacheEntry entry,
        bool eviction)
    {
        if (entry.Retired)
        {
            return;
        }

        entry.RemoveFrom(state.Entries!);
        state.LeastRecentlyUsed!.Remove(entry.Node!);
        entry.Node = null;
        entry.Retired = true;
        state.LiveEntries--;
        if (eviction)
        {
            ZoneTreePointCacheState.Increment(ref state.Evictions);
        }

        if (entry.PinCount == 0)
        {
            ReleaseEntry(state, entry);
        }
        else
        {
            state.RetiredPinnedEntries++;
        }
    }

    internal static void ReleaseEntry(ZoneTreePointCacheState state, ZoneTreePointCacheEntry entry)
    {
        state.ChargedEntries--;
        state.RetainedBytes -= entry.Reservation.Bytes;
        entry.ReleaseCharge();
    }

    internal static void ClearEntries(ZoneTreePointCacheState state)
    {
        while (state.LeastRecentlyUsed?.Last is { } last)
        {
            Retire(state, last.Value, eviction: false);
        }
    }

    internal static void AdvanceRevision(ZoneTreePointCacheState state)
    {
        if (state.Revision == long.MaxValue)
        {
            state.Enabled = false;
            ClearEntries(state);
            return;
        }

        state.Revision++;
    }

    internal static bool ObserveGeneration(ZoneTreePointCacheState state, long generation)
    {
        if (!state.HasReadGeneration)
        {
            state.ReadGeneration = generation;
            state.HasReadGeneration = true;
            return state.IsIndexAvailable();
        }

        if (generation == state.ReadGeneration)
        {
            return state.IsIndexAvailable();
        }

        if (generation < state.ReadGeneration)
        {
            return false;
        }

        state.ReadGeneration = generation;
        AdvanceRevision(state);
        ClearEntries(state);
        return state.IsIndexAvailable();
    }
}

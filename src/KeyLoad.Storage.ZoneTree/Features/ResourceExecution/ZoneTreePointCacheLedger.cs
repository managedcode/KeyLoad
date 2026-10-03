namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>Maintains owner and shared-budget accounting for fills and published entries.</summary>
internal static class ZoneTreePointCacheLedger
{
    internal static bool HasCapacity(ZoneTreePointCacheState state, long charge)
    {
        return state.ChargedEntries < state.Options.MaxEntries
            && state.RetainedBytes <= state.Options.MaxRetainedBytes - charge;
    }

    internal static long EntryCharge(int keyLength, int valueLength)
    {
        return ZoneTreePointCacheState.EntryMetadataBytes + RoundToEight(keyLength)
            + RoundToEight(valueLength);
    }

    internal static void AddEntry(ZoneTreePointCacheState state,
        ZoneTreePointCacheCandidate candidate, ZoneTreePointCacheEntry entry)
    {
        var added = false;
        try
        {
            entry.AddTo(state.Entries!);
            added = true;
            entry.Node = state.LeastRecentlyUsed!.AddFirst(entry);
        }
        catch (Exception)
        {
            if (added)
            {
                entry.RemoveFrom(state.Entries!);
            }

            throw;
        }

        entry.AdoptCharge();
        candidate.MarkAdopted();
        state.InFlightEntries--;
        state.LiveEntries++;
        ZoneTreePointCacheState.Increment(ref state.Admissions);
    }

    internal static void ReleaseCandidate(ZoneTreePointCacheState state,
        ZoneTreePointCacheCandidate candidate, bool countBypass)
    {
        var reservation = candidate.MarkReleased();
        if (reservation is null)
        {
            return;
        }

        state.InFlightEntries--;
        state.ChargedEntries--;
        state.RetainedBytes -= reservation.Bytes;
        reservation.Dispose();
        if (countBypass)
        {
            ZoneTreePointCacheState.Increment(ref state.AdmissionBypasses);
        }
    }

    private static long RoundToEight(int length)
    {
        return (length + 7L) & ~7L;
    }
}

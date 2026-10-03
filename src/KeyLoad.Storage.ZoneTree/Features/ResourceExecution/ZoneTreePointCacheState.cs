namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>One cache owner's synchronized admission, index and diagnostic state.</summary>
internal sealed class ZoneTreePointCacheState : IDisposable
{
    internal const int MaximumVictimAttempts = 16;
    internal const long EntryMetadataBytes = 320;
    internal readonly object Gate = new();
    internal readonly ZoneTreePointCacheOptions Options;
    internal Dictionary<byte[], ZoneTreePointCacheEntry>? Entries;
    internal LinkedList<ZoneTreePointCacheEntry>? LeastRecentlyUsed;
    internal ICacheMemoryReservation? IndexReservation;
    internal long RetainedBytes;
    internal int ChargedEntries;
    internal int LiveEntries;
    internal int RetiredPinnedEntries;
    internal int InFlightEntries;
    internal int ActivePins;
    internal long Revision;
    internal long ReadGeneration;
    internal long Hits;
    internal long Misses;
    internal long ReadBypasses;
    internal long NativeLookups;
    internal long NativeMisses;
    internal long AdmissionBypasses;
    internal long Admissions;
    internal long Evictions;
    internal long EvictionAttempts;
    internal bool Enabled;
    internal bool Closed;
    internal bool HasReadGeneration;

    internal ZoneTreePointCacheState(ZoneTreePointCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        Options = options;
        if (!options.MemoryBudget.TryReserve(options.IndexChargeBytes, 0, out var reservation))
        {
            return;
        }

        try
        {
            Entries = new Dictionary<byte[], ZoneTreePointCacheEntry>(options.MaxEntries,
                ZoneTreePointCacheByteComparer.Instance);
            LeastRecentlyUsed = new LinkedList<ZoneTreePointCacheEntry>();
            IndexReservation = reservation;
            RetainedBytes = reservation!.Bytes;
            Enabled = true;
        }
        catch (Exception)
        {
            reservation!.Dispose();
            throw;
        }
    }

    internal bool IsIndexAvailable()
    {
        return !Closed && Enabled && Entries is not null && IndexReservation is not null;
    }

    internal ZoneTreePointCacheSnapshot CreateSnapshot()
    {
        return new ZoneTreePointCacheSnapshot(true, IsIndexAvailable(), Closed, RetainedBytes,
            ChargedEntries, LiveEntries, RetiredPinnedEntries, InFlightEntries, ActivePins,
            Hits, Misses, ReadBypasses, NativeLookups, NativeMisses, AdmissionBypasses,
            Admissions, Evictions, EvictionAttempts);
    }

    public void Dispose()
    {
        ZoneTreePointCacheReads.Dispose(this);
    }

    internal static void Increment(ref long value)
    {
        if (value < long.MaxValue)
        {
            value++;
        }
    }
}

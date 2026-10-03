using System.Diagnostics.CodeAnalysis;

namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>Pinning and invalidation operations, all serialized by the cache gate.</summary>
internal static class ZoneTreePointCacheReads
{
    internal static bool TryPin(ZoneTreePointCacheState state, ReadOnlySpan<byte> key, long generation,
        [NotNullWhen(true)] out ZoneTreePointCacheEntry? entry)
    {
        lock (state.Gate)
        {
            entry = null;
            if (!ZoneTreePointCacheRetirement.ObserveGeneration(state, generation)
                || key.Length > state.Options.MaxKeyBytes)
            {
                ZoneTreePointCacheState.Increment(ref state.ReadBypasses);
                return false;
            }

            var lookup = state.Entries!.GetAlternateLookup<ReadOnlySpan<byte>>();
            if (!lookup.TryGetValue(key, out var found))
            {
                ZoneTreePointCacheState.Increment(ref state.Misses);
                return false;
            }

            if (found.Generation != generation)
            {
                ZoneTreePointCacheRetirement.Retire(state, found, eviction: false);
                ZoneTreePointCacheState.Increment(ref state.Misses);
                return false;
            }

            if (found.PinCount >= state.Options.MaxPinsPerEntry)
            {
                ZoneTreePointCacheState.Increment(ref state.ReadBypasses);
                return false;
            }

            found.PinCount++;
            state.ActivePins++;
            var lru = state.LeastRecentlyUsed!;
            lru.Remove(found.Node!);
            lru.AddFirst(found.Node!);
            ZoneTreePointCacheState.Increment(ref state.Hits);
            entry = found;
            return true;
        }
    }

    internal static void Unpin(ZoneTreePointCacheState state, IZoneTreePointCacheCandidateOwner owner,
        ZoneTreePointCacheEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsOwnedBy(owner))
        {
            throw new ArgumentException("The cache entry belongs to a different owner.", nameof(entry));
        }

        lock (state.Gate)
        {
            if (entry.PinCount <= 0)
            {
                throw new InvalidOperationException("The cache entry has no active pin.");
            }

            entry.PinCount--;
            state.ActivePins--;
            if (entry.PinCount == 0 && entry.Retired)
            {
                state.RetiredPinnedEntries--;
                ZoneTreePointCacheRetirement.ReleaseEntry(state, entry);
            }
        }
    }

    internal static void Invalidate(ZoneTreePointCacheState state, ReadOnlySpan<byte> key)
    {
        lock (state.Gate)
        {
            if (state.Closed || key.Length > state.Options.MaxKeyBytes)
            {
                return;
            }

            ZoneTreePointCacheRetirement.AdvanceRevision(state);
            if (state.Entries is not null)
            {
                var lookup = state.Entries.GetAlternateLookup<ReadOnlySpan<byte>>();
                if (lookup.TryGetValue(key, out var entry))
                {
                    ZoneTreePointCacheRetirement.Retire(state, entry, eviction: false);
                }
            }
        }
    }

    internal static void Clear(ZoneTreePointCacheState state)
    {
        lock (state.Gate)
        {
            if (!state.Closed)
            {
                ZoneTreePointCacheRetirement.AdvanceRevision(state);
                ZoneTreePointCacheRetirement.ClearEntries(state);
            }
        }
    }

    internal static void Disable(ZoneTreePointCacheState state)
    {
        lock (state.Gate)
        {
            if (!state.Closed)
            {
                state.Enabled = false;
                ZoneTreePointCacheRetirement.AdvanceRevision(state);
                ZoneTreePointCacheRetirement.ClearEntries(state);
            }
        }
    }

    internal static void Dispose(ZoneTreePointCacheState state)
    {
        lock (state.Gate)
        {
            if (state.Closed)
            {
                return;
            }

            state.Closed = true;
            state.Enabled = false;
            ZoneTreePointCacheRetirement.AdvanceRevision(state);
            ZoneTreePointCacheRetirement.ClearEntries(state);
            state.Entries = null;
            state.LeastRecentlyUsed = null;
            var reservation = state.IndexReservation;
            state.IndexReservation = null;
            if (reservation is not null)
            {
                state.RetainedBytes -= reservation.Bytes;
                reservation.Dispose();
            }
        }
    }
}

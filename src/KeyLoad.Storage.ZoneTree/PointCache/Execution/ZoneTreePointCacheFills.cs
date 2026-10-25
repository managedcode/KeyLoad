namespace KeyLoad.Storage.ZoneTree.ResourceExecution;

/// <summary>Bounded fill admission and generation-checked candidate publication.</summary>
internal static class ZoneTreePointCacheFills
{
    private const string ForeignCandidateMessage = "The fill candidate belongs to a different owner.";
    private const string ReservedLengthMismatchMessage = "The published value length must match its reserved length.";

    internal static (bool HasValue, ZoneTreePointCacheCandidate? Value) TryPrepare(ZoneTreePointCacheState state,
        IZoneTreePointCacheCandidateOwner owner, ReadOnlySpan<byte> key, int valueLength, long generation)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(valueLength);

        lock (state.Gate)
        {
            if (!ZoneTreePointCacheRetirement.ObserveGeneration(state, generation)
                || key.Length > state.Options.MaxKeyBytes
                || valueLength > state.Options.MaxValueBytes)
            {
                ZoneTreePointCacheState.Increment(ref state.AdmissionBypasses);
                return (false, default(ZoneTreePointCacheCandidate?));
            }

            var charge = ZoneTreePointCacheLedger.EntryCharge(key.Length, valueLength);
            ICacheMemoryReservation? reservation = null;
            try
            {
                if (!TryReserveWithEviction(state, charge, out reservation))
                {
                    ZoneTreePointCacheState.Increment(ref state.AdmissionBypasses);
                    return (false, default(ZoneTreePointCacheCandidate?));
                }

                var candidate = CreateCandidate(state, owner, key, valueLength, generation, reservation);
                reservation = null;
                return (true, (candidate));
            }
            finally
            {
                reservation?.Dispose();
            }
        }
    }

    internal static void Publish(ZoneTreePointCacheState state, IZoneTreePointCacheCandidateOwner owner,
        ZoneTreePointCacheCandidate candidate, ReadOnlySpan<byte> value, long generation)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.IsOwnedBy(owner))
        {
            throw new ArgumentException(ForeignCandidateMessage, nameof(candidate));
        }

        if (value.Length != candidate.ValueLength)
        {
            candidate.Dispose();
            throw new ArgumentException(ReservedLengthMismatchMessage, nameof(value));
        }

        PublishOwnedValue(state, owner, candidate, value, generation);
    }

    internal static void DisposeCandidate(ZoneTreePointCacheState state,
        ZoneTreePointCacheCandidate candidate)
    {
        lock (state.Gate)
        {
            ZoneTreePointCacheLedger.ReleaseCandidate(state, candidate, countBypass: false);
        }
    }

    private static bool CanPublish(ZoneTreePointCacheState state,
        ZoneTreePointCacheCandidate candidate, long generation)
    {
        return state.IsIndexAvailable() && state.HasReadGeneration && state.ReadGeneration == generation
            && candidate.IsPending
            && candidate.Revision == state.Revision && candidate.Generation == generation;
    }

    private static bool TryReserveWithEviction(ZoneTreePointCacheState state,
        long charge, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ICacheMemoryReservation? reservation)
    {
        for (var attempts = 0; attempts <= state.Options.MaximumVictimAttempts; attempts++)
        {
            if (ZoneTreePointCacheLedger.HasCapacity(state, charge)
                && state.Options.MemoryBudget.TryReserve(charge, 1, out reservation))
            {
                return reservation is not null;
            }

            if (attempts == state.Options.MaximumVictimAttempts
                || !ZoneTreePointCacheRetirement.TryRetireVictim(state))
            {
                reservation = null;
                return false;
            }

            ZoneTreePointCacheState.Increment(ref state.EvictionAttempts);
        }

        reservation = null;
        return false;
    }

    private static ZoneTreePointCacheCandidate CreateCandidate(ZoneTreePointCacheState state,
        IZoneTreePointCacheCandidateOwner owner, ReadOnlySpan<byte> key, int valueLength, long generation,
        ICacheMemoryReservation reservation)
    {
        try
        {
            var candidate = new ZoneTreePointCacheCandidate(owner, key.ToArray(), valueLength,
                generation, state.Revision, reservation);
            state.RetainedBytes += reservation.Bytes;
            state.ChargedEntries++;
            state.InFlightEntries++;
            return candidate;
        }
        catch (Exception)
        {
            reservation.Dispose();
            throw;
        }
    }

    private static void PublishOwnedValue(ZoneTreePointCacheState state,
        IZoneTreePointCacheCandidateOwner owner,
        ZoneTreePointCacheCandidate candidate, ReadOnlySpan<byte> value, long generation)
    {
        lock (state.Gate)
        {
            if (!ZoneTreePointCacheRetirement.ObserveGeneration(state, generation)
                || !CanPublish(state, candidate, generation))
            {
                ZoneTreePointCacheLedger.ReleaseCandidate(state, candidate, countBypass: true);
                return;
            }

            var lookup = state.Entries!.GetAlternateLookup<ReadOnlySpan<byte>>();
            if (lookup.TryGetValue(candidate.KeySpan, out var existing))
            {
                if (existing.Generation != generation)
                {
                    ZoneTreePointCacheRetirement.Retire(state, existing, eviction: false);
                }
                else
                {
                    ZoneTreePointCacheLedger.ReleaseCandidate(state, candidate, countBypass: true);
                    return;
                }
            }

            ZoneTreePointCacheEntry? entry = null;
            try
            {
                var ownedValue = value.ToArray();
                entry = candidate.CreateEntry(owner, ownedValue, generation);
                ZoneTreePointCacheLedger.AddEntry(state, candidate, entry);
                entry = null;
            }
            finally
            {
                entry?.Dispose();
            }
        }
    }
}

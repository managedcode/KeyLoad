namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>Owns exact local cache bindings; callers provide the real store writer boundary.</summary>
internal sealed class ZoneTreePointCacheControlState
{
    internal readonly object TransitionGate = new();
    internal readonly ZoneTreePointCacheOptions Options;
    internal readonly ICacheReadPermit Permit;
    internal ZoneTreePointCache? MaintenanceCacheValue;
    internal ZoneTreePointCacheBinding? Binding;
    internal bool Closed;

    internal ZoneTreePointCacheControlState(ZoneTreePointCacheOptions options, ICacheReadPermit permit)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(permit);
        options.Validate();
        Options = options;
        Permit = permit;
    }

    internal ZoneTreePointCache? MaintenanceCache => Volatile.Read(ref MaintenanceCacheValue);
    internal bool IsClosed => Volatile.Read(ref Closed);

    internal ZoneTreePointCacheSnapshot Snapshot()
    {
        var binding = Volatile.Read(ref Binding);
        var cache = Volatile.Read(ref MaintenanceCacheValue);
        var closed = Volatile.Read(ref Closed);
        var snapshot = cache?.Snapshot() ?? EmptySnapshot(closed);
        var enabled = !closed && binding is { Retired: false }
            && ReferenceEquals(binding, Volatile.Read(ref Binding))
            && Permit.IsCurrentAcceptance(binding.Acceptance) && snapshot.Enabled;
        return snapshot with { Enabled = enabled, Closed = closed };
    }

    internal ZoneTreePointCacheControlResult ApplyUnderWrite(CacheReadPermitAcceptance receipt)
    {
        return ZoneTreePointCacheControlTransitions.ApplyUnderWrite(this, receipt);
    }

    internal bool Retire(long withdrawnRevision)
    {
        if (withdrawnRevision <= 0)
        {
            return false;
        }

        ZoneTreePointCacheBinding? retired;
        lock (TransitionGate)
        {
            var current = Binding;
            if (Closed || current is null || current.Retired
                || current.Acceptance.Revision > withdrawnRevision
                || Permit.IsCurrentAcceptance(current.Acceptance))
            {
                return false;
            }

            retired = new ZoneTreePointCacheBinding(current.Acceptance, current.Cache, true);
            Volatile.Write(ref Binding, retired);
        }

        retired.Cache.Disable();
        return true;
    }

    internal void CloseAdmission()
    {
        ZoneTreePointCache? cache;
        lock (TransitionGate)
        {
            if (Closed)
            {
                return;
            }

            Volatile.Write(ref Closed, true);
            var current = Binding;
            if (current is not null && !current.Retired)
            {
                Volatile.Write(ref Binding,
                    new ZoneTreePointCacheBinding(current.Acceptance, current.Cache, true));
            }

            cache = MaintenanceCacheValue;
        }

        cache?.Disable();
    }

    internal void DisposeUnderWrite()
    {
        CloseAdmission();
        ZoneTreePointCache? cache;
        lock (TransitionGate)
        {
            cache = MaintenanceCacheValue;
            Volatile.Write(ref Binding, null);
        }

        cache?.Dispose();
    }

    internal ZoneTreePointCacheReadAdmission CaptureReadAdmission()
    {
        return new ZoneTreePointCacheReadAdmission(this, Volatile.Read(ref Binding));
    }

    internal bool IsCurrent(ZoneTreePointCacheBinding? binding)
    {
        return binding is { Retired: false }
            && !Volatile.Read(ref Closed)
            && ReferenceEquals(binding, Volatile.Read(ref Binding))
            && Permit.IsCurrentAcceptance(binding.Acceptance);
    }

    internal void PublishIfCurrent(ZoneTreePointCacheBinding binding,
        ZoneTreePointCacheCandidate candidate, ReadOnlySpan<byte> value, long generation)
    {
        lock (TransitionGate)
        {
            if (Closed || !ReferenceEquals(binding, Binding) || binding.Retired
                || !Permit.IsCurrentAcceptance(binding.Acceptance))
            {
                return;
            }

            binding.Cache.Publish(candidate, value, generation);
        }
    }

    private static ZoneTreePointCacheSnapshot EmptySnapshot(bool closed)
    {
        return new ZoneTreePointCacheSnapshot(true, false, closed, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0);
    }
}

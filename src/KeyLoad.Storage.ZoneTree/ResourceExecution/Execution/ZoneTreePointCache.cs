using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;

namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>Bounded disposable point-value cache for one physical ZoneTree runtime.</summary>
internal sealed class ZoneTreePointCache : IDisposable, IZoneTreePointCacheCandidateOwner
{
    private readonly ZoneTreePointCacheState _state;
    private readonly ZoneTreePointCacheExecutionOptions? executionPolicy;

    internal ZoneTreePointCache(ZoneTreePointCacheOptions options, IOptions<ZoneTreePointCacheExecutionOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(executionOptions);
        executionPolicy = executionOptions.Value;
        ArgumentNullException.ThrowIfNull(executionPolicy);
        executionPolicy.Validate();
        _state = new ZoneTreePointCacheState(options.WithExecutionSnapshot(executionPolicy));
    }

    private ZoneTreePointCache(ZoneTreePointCacheState resolvedState) => _state = resolvedState;

    internal static ZoneTreePointCache CreateResolved(ZoneTreePointCacheOptions options)
    {
        _ = options.MaximumVictimAttempts;
        return new(new ZoneTreePointCacheState(options));
    }

    internal bool TryPin(ReadOnlySpan<byte> key, long generation,
        [NotNullWhen(true)] out ZoneTreePointCacheEntry? entry)
    {
        return ZoneTreePointCacheReads.TryPin(_state, key, generation, out entry);
    }

    internal void Unpin(ZoneTreePointCacheEntry entry)
    {
        ZoneTreePointCacheReads.Unpin(_state, this, entry);
    }

    internal ZoneTreePointCacheCandidate? TryPrepare(ReadOnlySpan<byte> key, int valueLength,
        long generation)
    {
        return ZoneTreePointCacheFills.TryPrepare(_state, this, key, valueLength, generation);
    }

    internal void Publish(ZoneTreePointCacheCandidate candidate, ReadOnlySpan<byte> value,
        long generation)
    {
        ZoneTreePointCacheFills.Publish(_state, this, candidate, value, generation);
    }

    internal void Invalidate(ReadOnlySpan<byte> key)
    {
        ZoneTreePointCacheReads.Invalidate(_state, key);
    }

    internal void Clear()
    {
        ZoneTreePointCacheReads.Clear(_state);
    }

    internal void Disable()
    {
        ZoneTreePointCacheReads.Disable(_state);
    }

    internal void RecordNativeLookup(bool found)
    {
        lock (_state.Gate)
        {
            ZoneTreePointCacheState.Increment(ref _state.NativeLookups);
            if (!found)
            {
                ZoneTreePointCacheState.Increment(ref _state.NativeMisses);
            }
        }
    }

    internal ZoneTreePointCacheSnapshot Snapshot()
    {
        lock (_state.Gate)
        {
            return _state.CreateSnapshot();
        }
    }

    void IZoneTreePointCacheCandidateOwner.DisposeCandidate(ZoneTreePointCacheCandidate candidate)
    {
        ZoneTreePointCacheFills.DisposeCandidate(_state, candidate);
    }

    public void Dispose()
    {
        _state.Dispose();
    }
}

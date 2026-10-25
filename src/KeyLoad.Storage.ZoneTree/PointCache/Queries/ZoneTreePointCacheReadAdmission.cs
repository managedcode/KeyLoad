using System.Diagnostics.CodeAnalysis;

namespace KeyLoad.Storage.ZoneTree.ResourceExecution;

/// <summary>Captures one local binding without allocating a lease for each point read.</summary>
internal readonly struct ZoneTreePointCacheReadAdmission
{
    private readonly ZoneTreePointCacheControlState? owner;
    private readonly ZoneTreePointCacheBinding? binding;
    private readonly ZoneTreePointCache? embedded;

    internal ZoneTreePointCacheReadAdmission(ZoneTreePointCacheControlState owner, ZoneTreePointCacheBinding? binding)
    {
        this.owner = owner;
        this.binding = binding;
        embedded = null;
    }

    internal ZoneTreePointCacheReadAdmission(ZoneTreePointCache? embedded)
    {
        this.embedded = embedded;
        owner = null;
        binding = null;
    }

    internal ZoneTreePointCache? Cache => binding?.Cache ?? embedded;

    internal bool TryPin(ReadOnlySpan<byte> key, long generation,
        [NotNullWhen(true)] out ZoneTreePointCacheEntry? entry)
    {
        entry = null;
        var cache = Cache;
        if (cache is null || !IsCurrent() || !cache.TryPin(key, generation, out entry))
        {
            return false;
        }
        if (IsCurrent())
        {
            return true;
        }

        cache.Unpin(entry);
        entry = null;
        return false;
    }

    internal void Unpin(ZoneTreePointCacheEntry entry) => Cache!.Unpin(entry);

    internal global::ManagedCode.Communication.Result<ZoneTreePointCacheCandidate?> TryPrepare(ReadOnlySpan<byte> key, int valueLength, long generation)
        => ((ZoneTreePointCacheCandidate?)(IsCurrent() ? Cache?.TryPrepare(key, valueLength, generation) : null)) is { } outcomeValue1 ? global::ManagedCode.Communication.Result<ZoneTreePointCacheCandidate?>.Succeed(outcomeValue1) : global::ManagedCode.Communication.Result<ZoneTreePointCacheCandidate?>.FailNotFound(nameof(TryPrepare));

    internal void Publish(ZoneTreePointCacheCandidate candidate, ReadOnlySpan<byte> value, long generation)
    {
        if (owner is not null && binding is not null)
        {
            owner.PublishIfCurrent(binding, candidate, value, generation);
        }
        else
        {
            embedded?.Publish(candidate, value, generation);
        }
    }

    internal void RecordLookup(bool found) => Cache?.RecordLookup(found);

    private bool IsCurrent() => owner is null ? embedded is not null : owner.IsCurrent(binding);
}

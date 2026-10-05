namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

internal interface IZoneTreePointCacheCandidateOwner
{
    void DisposeCandidate(ZoneTreePointCacheCandidate candidate);
}

/// <summary>A privately owned positive value whose bytes remain charged while pinned.</summary>
internal sealed class ZoneTreePointCacheEntry : IDisposable
{
    private const int NoActivePins = 0;
    private const string LiveChargeReleaseMessage = "A live or pinned cache entry cannot release its charge.";

    private readonly IZoneTreePointCacheCandidateOwner _owner;
    private readonly byte[] _key;
    private readonly byte[] _buffer;
    private bool _disposed;
    private bool _ownsCharge;

    internal ZoneTreePointCacheEntry(IZoneTreePointCacheCandidateOwner owner, byte[] key,
        byte[] value, long generation,
        ICacheMemoryReservation reservation)
    {
        _owner = owner;
        _key = key;
        _buffer = value;
        Generation = generation;
        Reservation = reservation;
    }

    internal ReadOnlySpan<byte> KeySpan => _key;

    internal long Generation { get; }

    internal ICacheMemoryReservation Reservation { get; }

    internal LinkedListNode<ZoneTreePointCacheEntry>? Node { get; set; }

    internal int PinCount { get; set; }

    internal bool Retired { get; set; }

    internal bool IsOwnedBy(IZoneTreePointCacheCandidateOwner owner)
    {
        return ReferenceEquals(_owner, owner);
    }

    internal void AddTo(Dictionary<byte[], ZoneTreePointCacheEntry> index)
    {
        index.Add(_key, this);
    }

    internal void RemoveFrom(Dictionary<byte[], ZoneTreePointCacheEntry> index)
    {
        index.GetAlternateLookup<ReadOnlySpan<byte>>().Remove(_key);
    }

    /// <summary>Borrowed bytes; callers must retain a cache pin for the entire span use.</summary>
    internal ReadOnlySpan<byte> Value => _buffer;

    internal void AdoptCharge() => _ownsCharge = true;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_ownsCharge && (!Retired || PinCount != NoActivePins))
        {
            throw new InvalidOperationException(LiveChargeReleaseMessage);
        }

        _disposed = true;
        if (_ownsCharge)
        {
            Reservation.Dispose();
        }
    }

    internal void ReleaseCharge()
    {
        Dispose();
    }
}

/// <summary>Owns a reserved key/fill charge until publication or idempotent disposal.</summary>
internal sealed class ZoneTreePointCacheCandidate : IDisposable
{
    private const int PendingState = 0;
    private const int AdoptedState = 1;
    private const int ReleasedState = 2;

    private readonly IZoneTreePointCacheCandidateOwner _owner;
    private byte[]? _key;
    private ICacheMemoryReservation? _reservation;
    private int _state;

    internal ZoneTreePointCacheCandidate(IZoneTreePointCacheCandidateOwner owner, byte[] key,
        int valueLength,
        long generation, long revision, ICacheMemoryReservation reservation)
    {
        _owner = owner;
        _key = key;
        ValueLength = valueLength;
        Generation = generation;
        Revision = revision;
        _reservation = reservation;
    }

    internal int ValueLength { get; }

    internal long Generation { get; }

    internal long Revision { get; }

    internal ReadOnlySpan<byte> KeySpan => _key ?? Array.Empty<byte>();

    internal ICacheMemoryReservation? Reservation => _reservation;

    internal bool IsPending => _state == PendingState;

    internal ZoneTreePointCacheEntry CreateEntry(IZoneTreePointCacheCandidateOwner owner,
        byte[] value, long generation)
    {
        return new ZoneTreePointCacheEntry(owner, _key!, value, generation, _reservation!);
    }

    public void Dispose()
    {
        _owner.DisposeCandidate(this);
    }

    internal bool IsOwnedBy(IZoneTreePointCacheCandidateOwner owner)
    {
        return ReferenceEquals(_owner, owner);
    }

    internal void MarkAdopted()
    {
        _state = AdoptedState;
        _key = null;
        _reservation = null;
    }

    internal ICacheMemoryReservation? MarkReleased()
    {
        if (_state != PendingState)
        {
            return null;
        }

        _state = ReleasedState;
        _key = null;
        var reservation = _reservation;
        _reservation = null;
        return reservation;
    }
}

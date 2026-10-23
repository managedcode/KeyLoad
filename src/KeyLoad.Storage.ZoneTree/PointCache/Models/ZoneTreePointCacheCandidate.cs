namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>Owns a reserved key/fill charge until publication or idempotent disposal.</summary>
internal sealed class ZoneTreePointCacheCandidate : IDisposable
{
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

    internal int ValueLength
    {
        get;
    }

    internal long Generation
    {
        get;
    }

    internal long Revision
    {
        get;
    }

    internal ReadOnlySpan<byte> KeySpan => _key ?? Array.Empty<byte>();

    internal ICacheMemoryReservation? Reservation => _reservation;

    internal bool IsPending => _state == 0;

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
        _state = 1;
        _key = null;
        _reservation = null;
    }

    internal ICacheMemoryReservation? MarkReleased()
    {
        if (_state != 0)
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

namespace KeyLoad.Storage.ZoneTree.ResourceExecution;

/// <summary>A privately owned positive value whose bytes remain charged while pinned.</summary>
internal sealed class ZoneTreePointCacheEntry : IDisposable
{
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

    internal long Generation
    {
        get;
    }

    internal ICacheMemoryReservation Reservation
    {
        get;
    }

    internal LinkedListNode<ZoneTreePointCacheEntry>? Node
    {
        get; set;
    }

    internal int PinCount
    {
        get; set;
    }

    internal bool Retired
    {
        get; set;
    }

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

        if (_ownsCharge && (!Retired || PinCount != 0))
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

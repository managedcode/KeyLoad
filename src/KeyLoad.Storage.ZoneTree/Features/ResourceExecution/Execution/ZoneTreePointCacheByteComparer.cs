namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>Compares encoded keys by complete byte content without allocating span lookups.</summary>
internal sealed class ZoneTreePointCacheByteComparer : IEqualityComparer<byte[]>,
    IAlternateEqualityComparer<ReadOnlySpan<byte>, byte[]>
{
    public static ZoneTreePointCacheByteComparer Instance { get; } = new();

    private ZoneTreePointCacheByteComparer()
    {
    }

    public bool Equals(byte[]? x, byte[]? y)
    {
        return ReferenceEquals(x, y) || (x is not null && y is not null && x.AsSpan().SequenceEqual(y));
    }

    public int GetHashCode(byte[] key)
    {
        return GetHashCode(key.AsSpan());
    }

    public bool Equals(ReadOnlySpan<byte> alternate, byte[] key)
    {
        return alternate.SequenceEqual(key);
    }

    public int GetHashCode(ReadOnlySpan<byte> alternate)
    {
        var hash = new HashCode();
        hash.AddBytes(alternate);
        return hash.ToHashCode();
    }

    public byte[] Create(ReadOnlySpan<byte> alternate)
    {
        return alternate.ToArray();
    }
}

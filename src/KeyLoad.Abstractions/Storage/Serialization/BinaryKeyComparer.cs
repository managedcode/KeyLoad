namespace KeyLoad.Storage;

/// <summary>Compares encoded keys using unsigned lexicographic byte ordering.</summary>
public sealed class BinaryKeyComparer : IComparer<byte[]>
{
    /// <summary>Gets the shared binary key comparer.</summary>
    public static BinaryKeyComparer Instance { get; } = new();

    /// <summary>Compares two byte arrays lexicographically; null is treated as an empty array.</summary>
    /// <param name="x">The first key to compare.</param>
    /// <param name="y">The second key to compare.</param>
    /// <returns>A negative value when <paramref name="x"/> precedes <paramref name="y"/>, zero when they are equal, or a positive value otherwise.</returns>
    public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y.AsSpan());
}

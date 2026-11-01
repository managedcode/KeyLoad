namespace KeyLoad.Storage;

/// <summary>Computes byte-order bounds shared by storage traversal and index planning.</summary>
internal static class StorageKeyBounds
{
    internal static (bool HasValue, byte[]? Value) PrefixSuccessor(ReadOnlySpan<byte> prefix)
    {
        if (prefix.IsEmpty)
        {
            return (false, default(byte[]?));
        }
        var successor = prefix.ToArray();
        for (var index = successor.Length - 1; index >= 0; index--)
        {
            if (successor[index] == byte.MaxValue)
            {
                continue;
            }
            successor[index]++;
            return (true, successor[..(index + 1)]);
        }
        return (false, default(byte[]?));
    }
}

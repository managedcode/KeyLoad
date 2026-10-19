namespace KeyLoad.Server;

/// <summary>Named conservative capacity equations used by the MCP retained-memory projection.</summary>
internal static class McpMemoryProjectionComponents
{
    private const long MinimumCapacity = 16;
    private const long MetadataBaseBytes = 4096;
    private const long MetadataPerTokenBytes = 12;
    private const long MetadataEntryBytes = 12;
    private const long MetadataCompletionEntries = 1;
    private const long WriterSlackBytes = 256;
    private const long WriterSourceOverlap = 2;
    private const long WriterBufferOverlap = 2;
    private const long StructurePerTokenBytes = 128;
    private const long StructurePerPropertyBytes = 128;
    private const long StructureDepthSlots = 64;
    private const long StructurePerDepthBytes = 256;
    private const long Utf16ExpansionBytes = 2;
    private const long EscapedCharacterExpansion = 6;
    private const long EscapingBufferOverlap = 2;

    /// <summary>Projects retained metadata arrays, growth/trim overlap, completion state and parser stack.</summary>
    /// <param name="bytes">The serialized source bytes.</param>
    /// <param name="tokens">The number of parsed tokens.</param>
    /// <returns>The conservative metadata byte charge.</returns>
    internal static long Metadata(long bytes, long tokens)
    {
        const int RoundCapacityScaleFactor = 2;

        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        ArgumentOutOfRangeException.ThrowIfNegative(tokens);
        var arrayCapacity = Math.Max(checked(bytes + MetadataEntryBytes),
            checked(MetadataPerTokenBytes * (tokens + MetadataCompletionEntries)));
        return checked(RoundCapacityScaleFactor * RoundCapacity(arrayCapacity) + MetadataPerTokenBytes * tokens + MetadataBaseBytes);
    }

    /// <summary>Projects native writer growth, maximum size hint and old/new buffer overlap.</summary>
    /// <param name="bytes">The current writer contents.</param>
    /// <param name="hint">The conservative maximum size hint passed to the writer.</param>
    /// <returns>The conservative writer byte charge.</returns>
    internal static long Writer(long bytes, long hint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        ArgumentOutOfRangeException.ThrowIfNegative(hint);
        return checked(WriterBufferOverlap * RoundCapacity(checked(WriterSourceOverlap * bytes + hint + WriterSlackBytes)));
    }

    /// <summary>Projects unescaped/escaped UTF-16 rents and replacement overlap.</summary>
    /// <param name="bytes">The maximum UTF-8 source length.</param>
    /// <returns>The conservative escaping byte charge.</returns>
    internal static long Escaping(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        return checked(RoundCapacity(bytes) + EscapingBufferOverlap *
            RoundCapacity(checked(EscapedCharacterExpansion * bytes + WriterSlackBytes)));
    }

    /// <summary>Projects native JSON values, containers, dictionaries, depth slots and decoded UTF-16 strings.</summary>
    /// <param name="bytes">The UTF-8 source bytes represented by the structure.</param>
    /// <param name="tokens">The bounded token count.</param>
    /// <param name="properties">The bounded property count.</param>
    /// <returns>The conservative structure byte charge.</returns>
    internal static long Structure(long bytes, long tokens, long properties)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        ArgumentOutOfRangeException.ThrowIfNegative(tokens);
        ArgumentOutOfRangeException.ThrowIfNegative(properties);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(properties, tokens);
        return checked(StructurePerTokenBytes * tokens + StructurePerPropertyBytes * properties +
                       StructurePerDepthBytes * StructureDepthSlots + Utf16ExpansionBytes * bytes);
    }

    private static long RoundCapacity(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        if (bytes <= MinimumCapacity)
        {
            return MinimumCapacity;
        }
        var rounded = System.Numerics.BitOperations.RoundUpToPowerOf2((ulong)bytes);
        const ulong PowerOfTwoOverflow = 0;
        if (rounded == PowerOfTwoOverflow || rounded > (ulong)long.MaxValue)
        {
            throw new OverflowException();
        }
        return (long)rounded;
    }
}

using static KeyLoad.Storage.KeyCodecReadPrimitives;
using static KeyLoad.Storage.KeyCodecTokens;

namespace KeyLoad.Storage;

/// <summary>Sortable binary keys; strings use UTF-8 byte order and zero escaping.</summary>
public static class KeyCodec
{
    /// <summary>Gets the framing marker written at the start of every encoded key.</summary>
    public const byte PrefixMarker = 1;
    internal const int MaximumComponents = 256;
    private const string TooManyComponentsMessage = "Key component count exceeds the supported limit.";
    private const int InitialKeyBytes = 256;

    /// <summary>Encodes typed components into a sortable binary key without boxing them.</summary>
    /// <param name="components">The key components, in their comparison order.</param>
    /// <returns>The encoded key bytes.</returns>
    /// <exception cref="KeyLoadException">A component cannot be encoded or the component count exceeds the limit.</exception>
    public static byte[] Encode(params ReadOnlySpan<KeyComponent> components)
        => Encode(components, []);

    /// <summary>Encodes a decoded key without a temporary component array.</summary>
    /// <param name="components">The decoded components in their comparison order.</param>
    /// <returns>The canonical key bytes.</returns>
    public static byte[] Encode(DecodedKey components)
    {
        Span<byte> initial = stackalloc byte[InitialKeyBytes];
        var writer = new KeyBufferWriter(initial);
        try
        {
            writer.Write(PrefixMarker);
            foreach (var component in components)
            {
                KeyCodecWritePrimitives.Write(ref writer, in component);
            }
            return writer.ToArray();
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>Writes a typed scope and dynamic components directly into the same canonical key buffer.</summary>
    internal static byte[] Encode(ReadOnlySpan<KeyComponent> prefix, ReadOnlySpan<KeyComponent> suffix)
    {
        if (prefix.Length > MaximumComponents || suffix.Length > MaximumComponents - prefix.Length)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, TooManyComponentsMessage);
        }

        Span<byte> initial = stackalloc byte[InitialKeyBytes];
        var writer = new KeyBufferWriter(initial);
        try
        {
            writer.Write(PrefixMarker);
            foreach (ref readonly var component in prefix)
            {
                KeyCodecWritePrimitives.Write(ref writer, in component);
            }
            foreach (ref readonly var component in suffix)
            {
                KeyCodecWritePrimitives.Write(ref writer, in component);
            }

            return writer.ToArray();
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>Appends one component to a trusted encoded prefix with room for that component.</summary>
    internal static byte[] AppendComponent(ReadOnlySpan<byte> encodedPrefix, in KeyComponent component)
    {
        Span<byte> initial = stackalloc byte[InitialKeyBytes];
        var writer = new KeyBufferWriter(initial);
        try
        {
            writer.Write(encodedPrefix);
            KeyCodecWritePrimitives.Write(ref writer, in component);
            return writer.ToArray();
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>Decodes a sortable binary key into its components.</summary>
    /// <param name="key">The encoded key bytes.</param>
    /// <returns>The decoded components in their original order.</returns>
    /// <exception cref="KeyLoadException">The key is unsupported or malformed.</exception>
    public static DecodedKey Decode(ReadOnlySpan<byte> key)
    {
        var /* KLD0041: This decoder materializes the closed native scalar set into compact reference-sized slots, exposed through DecodedKey. */ values = new object?[KeyCodecValidation.Count(key)] /* KLD0041: Native scalar slots avoid a large per-component struct array for text-heavy keys. */;
        var offset = PrefixBytes;
        for (var index = 0; index < values /* KLD0041: This is only the owned native slot count. */.Length; index++)
        {
            var tag = key[offset++];
            values /* KLD0041: Only the validated native tag writes this private compact scalar slot. */[index] = tag switch
            {
                MissingTag => MissingValue.Instance,
                NullTag => null,
                BooleanTag => (ReadByte(key, ref offset) switch
                {
                    FalsePayload => false,
                    TruePayload => true,
                    _ => throw BadKey()
                } /* KLD0042: The native boolean occupies one compact heterogeneous key slot; the public view restores its exact scalar type. */),
                Int64Tag => unchecked((long)(ReadUInt64(key, ref offset) ^ SortableSignMask) /* KLD0042: The native integer occupies one compact heterogeneous slot before typed access. */),
                DecimalTag => KeyCodecDecimal.Read(key, ref offset) /* KLD0042: The native decimal occupies one compact heterogeneous slot before typed access. */,
                DoubleTag => ReadDouble(key, ref offset) /* KLD0042: The native floating-point value occupies one compact heterogeneous slot before typed access. */,
                TimestampTag => ReadTimestamp(key, ref offset) /* KLD0042: The native timestamp occupies one compact heterogeneous slot before typed access. */,
                TextTag => ReadText(key, ref offset),
                BinaryTag => ReadEscaped(key, ref offset).ToArray(),
                _ => throw BadKey()
            };
        }
        return new(values) /* KLD0041: The typed collection owns the decoded native scalar slots without another heap wrapper. */;
    }
}

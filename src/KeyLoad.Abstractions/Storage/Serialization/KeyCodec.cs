using static KeyLoad.Storage.KeyCodecReadPrimitives;
using static KeyLoad.Storage.KeyCodecTokens;

namespace KeyLoad.Storage;

/// <summary>Version 1 binary keys; strings use UTF-8 byte order and zero escaping.</summary>
public static class KeyCodec
{
    /// <summary>Gets the version byte written at the start of every encoded key.</summary>
    public const byte Version = 1;
    private const int MaximumComponents = 256;
    private const string TooManyComponentsMessage = "Key component count exceeds the supported limit.";
    private const int InitialKeyBytes = 256;

    /// <summary>Encodes typed components into a version 1 sortable binary key without boxing them.</summary>
    /// <param name="components">The key components, in their comparison order.</param>
    /// <returns>The encoded key bytes.</returns>
    /// <exception cref="KeyLoadException">A component cannot be encoded or the component count exceeds the limit.</exception>
    public static byte[] Encode(params ReadOnlySpan<KeyComponent> components)
    {
        if (components.Length > MaximumComponents)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, TooManyComponentsMessage);
        }

        Span<byte> initial = stackalloc byte[InitialKeyBytes];
        var writer = new KeyBufferWriter(initial);
        try
        {
            writer.Write(Version);
            foreach (ref readonly var component in components)
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

    /// <summary>Encodes boxed components into a version 1 sortable binary key; the bytes equal the typed overload's.</summary>
    /// <param name="components">The key components, in their comparison order.</param>
    /// <returns>The encoded key bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="components"/> is null.</exception>
    /// <exception cref="KeyLoadException">A component is unsupported or cannot be encoded.</exception>
    public static byte[] Encode(object?[] components)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (components.Length > MaximumComponents)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, TooManyComponentsMessage);
        }

        Span<byte> initial = stackalloc byte[InitialKeyBytes];
        var writer = new KeyBufferWriter(initial);
        try
        {
            writer.Write(Version);
            foreach (var component in components)
            {
                var typed = KeyComponent.From(component);
                KeyCodecWritePrimitives.Write(ref writer, in typed);
            }

            return writer.ToArray();
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>Encodes one boxed component, such as a query value whose runtime type is only known when it is read.</summary>
    /// <param name="component">The boxed component, or null for the null component.</param>
    /// <returns>The encoded key bytes.</returns>
    /// <exception cref="KeyLoadException">The component is unsupported or cannot be encoded.</exception>
    public static byte[] Encode(object? component) => Encode(KeyComponent.From(component));

    /// <summary>Decodes a version 1 sortable binary key into its components.</summary>
    /// <param name="key">The encoded key bytes.</param>
    /// <returns>The decoded components in their original order.</returns>
    /// <exception cref="KeyLoadException">The key is unsupported or malformed.</exception>
    public static object?[] Decode(ReadOnlySpan<byte> key)
    {
        if (key.IsEmpty || key[VersionOffset] != Version)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, UnknownVersion);
        }

        var values = new List<object?>();
        var offset = VersionBytes;
        while (offset < key.Length)
        {
            if (values.Count == MaximumComponents)
            {
                throw BadKey();
            }

            var tag = key[offset++];
            object? value = tag switch
            {
                MissingTag => MissingValue.Instance,
                NullTag => null,
                BooleanTag => ReadByte(key, ref offset) switch { FalsePayload => false, TruePayload => true, _ => throw BadKey() },
                Int64Tag => unchecked((long)(ReadUInt64(key, ref offset) ^ SortableSignMask)),
                DecimalTag => KeyCodecDecimal.Read(key, ref offset),
                DoubleTag => ReadDouble(key, ref offset),
                TimestampTag => ReadTimestamp(key, ref offset),
                TextTag => ReadText(key, ref offset),
                BinaryTag => ReadEscaped(key, ref offset),
                _ => throw BadKey()
            };
            values.Add(value);
        }
        return values.ToArray();
    }
}

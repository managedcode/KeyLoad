using static KeyLoad.Storage.KeyCodecReadPrimitives;
using static KeyLoad.Storage.KeyCodecTokens;

namespace KeyLoad.Storage;

/// <summary>Version 1 binary keys; strings use UTF-8 byte order and zero escaping.</summary>
public static class KeyCodec
{
    /// <summary>Gets the version byte written at the start of every encoded key.</summary>
    public const byte Version = 1;
    internal const int MaximumComponents = 256;
    private const string TooManyComponentsMessage = "Key component count exceeds the supported limit.";
    private const int InitialKeyBytes = 256;

    /// <summary>Encodes typed components into a version 1 sortable binary key without boxing them.</summary>
    /// <param name="components">The key components, in their comparison order.</param>
    /// <returns>The encoded key bytes.</returns>
    /// <exception cref="KeyLoadException">A component cannot be encoded or the component count exceeds the limit.</exception>
    public static byte[] Encode(params ReadOnlySpan<KeyComponent> components)
        => Encode(components, []);

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
            writer.Write(Version);
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

    /// <summary>Decodes a version 1 sortable binary key into its components.</summary>
    /// <param name="key">The encoded key bytes.</param>
    /// <returns>The decoded components in their original order.</returns>
    /// <exception cref="KeyLoadException">The key is unsupported or malformed.</exception>
    public static KeyComponent[] Decode(ReadOnlySpan<byte> key)
    {
        var values = new KeyComponent[KeyCodecValidation.Validate(key)];
        var offset = VersionBytes;
        for (var index = 0; index < values.Length; index++)
        {
            var tag = key[offset++];
            values[index] = tag switch
            {
                MissingTag => KeyComponent.Missing,
                NullTag => KeyComponent.Null,
                BooleanTag => KeyComponent.FromBoolean(ReadByte(key, ref offset) switch
                {
                    FalsePayload => false,
                    TruePayload => true,
                    _ => throw BadKey()
                }),
                Int64Tag => KeyComponent.FromInt64(unchecked((long)(ReadUInt64(key, ref offset) ^ SortableSignMask))),
                DecimalTag => KeyComponent.FromDecimal(KeyCodecDecimal.Read(key, ref offset)),
                DoubleTag => KeyComponent.FromDouble(ReadDouble(key, ref offset)),
                TimestampTag => KeyComponent.FromDateTimeOffset(ReadTimestamp(key, ref offset)),
                TextTag => KeyComponent.FromString(ReadText(key, ref offset)),
                BinaryTag => KeyComponent.FromByteArray(ReadEscaped(key, ref offset).ToArray()),
                _ => throw BadKey()
            };
        }
        return values;
    }
}

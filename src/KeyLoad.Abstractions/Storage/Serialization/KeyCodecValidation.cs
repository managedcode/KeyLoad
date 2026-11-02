using System.Text.Unicode;
using static KeyLoad.Storage.KeyCodecReadPrimitives;
using static KeyLoad.Storage.KeyCodecTokens;

namespace KeyLoad.Storage;

/// <summary>Validates the complete encoded key without materializing its decoded components.</summary>
internal static class KeyCodecValidation
{
    internal static int Validate(ReadOnlySpan<byte> key)
        => Inspect(key, validate: true);

    /// <summary>Counts framed components without materializing values; Decode validates their scalar contents once.</summary>
    internal static int Count(ReadOnlySpan<byte> key)
        => Inspect(key, validate: false);

    private static int Inspect(ReadOnlySpan<byte> key, bool validate)
    {
        if (key.IsEmpty || key[VersionOffset] != KeyCodec.Version)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, UnknownVersion);
        }
        var components = 0;
        var offset = VersionBytes;
        while (offset < key.Length)
        {
            if (components++ == KeyCodec.MaximumComponents)
            {
                throw BadKey();
            }
            Component(key, ref offset, validate);
        }
        return components;
    }

    private static void Component(ReadOnlySpan<byte> key, ref int offset, bool validate)
    {
        switch (ReadByte(key, ref offset))
        {
            case MissingTag or NullTag:
                return;
            case BooleanTag:
                if (ReadByte(key, ref offset) is not (FalsePayload or TruePayload))
                {
                    throw BadKey();
                }
                return;
            case Int64Tag:
                _ = ReadUInt64(key, ref offset);
                return;
            case DecimalTag:
                if (validate)
                {
                    _ = KeyCodecDecimal.Read(key, ref offset);
                }
                else
                {
                    KeyCodecDecimal.Skip(key, ref offset);
                }
                return;
            case DoubleTag:
                _ = ReadDouble(key, ref offset);
                return;
            case TimestampTag:
                _ = ReadTimestamp(key, ref offset);
                return;
            case TextTag when validate:
                if (!Utf8.IsValid(ReadEscaped(key, ref offset)))
                {
                    throw BadKey();
                }
                return;
            case TextTag or BinaryTag:
                SkipEscaped(key, ref offset);
                return;
            default:
                throw BadKey();
        }
    }
}

using System.Text.Unicode;
using static KeyLoad.Storage.KeyCodecReadPrimitives;
using static KeyLoad.Storage.KeyCodecTokens;

namespace KeyLoad.Storage;

/// <summary>Validates the complete encoded key without materializing its decoded components.</summary>
internal static class KeyCodecValidation
{
    internal static int Validate(ReadOnlySpan<byte> key)
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
            Component(key, ref offset);
        }
        return components;
    }

    private static void Component(ReadOnlySpan<byte> key, ref int offset)
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
                _ = KeyCodecDecimal.Read(key, ref offset);
                return;
            case DoubleTag:
                _ = ReadDouble(key, ref offset);
                return;
            case TimestampTag:
                _ = ReadTimestamp(key, ref offset);
                return;
            case TextTag:
                if (!Utf8.IsValid(ReadEscaped(key, ref offset)))
                {
                    throw BadKey();
                }
                return;
            case BinaryTag:
                _ = ReadEscaped(key, ref offset);
                return;
            default:
                throw BadKey();
        }
    }
}

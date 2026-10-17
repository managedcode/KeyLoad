namespace KeyLoad.Storage;

// Frozen v1 wire identities; these values are not node admission or execution settings.
internal static class KeyCodecTokens
{
    internal const int VersionOffset = 0;
    internal const int VersionBytes = 1;
    internal const int MissingTag = 0x10;
    internal const int NullTag = 0x11;
    internal const int BooleanTag = 0x20;
    internal const int Int64Tag = 0x30;
    internal const int DecimalTag = 0x31;
    internal const int DoubleTag = 0x32;
    internal const int TimestampTag = 0x40;
    internal const int TextTag = 0x50;
    internal const int BinaryTag = 0x60;
    internal const int FalsePayload = 0;
    internal const int TruePayload = 1;
    internal const int ZeroNumber = 0;
    internal const int NoSetBits = 0;
    internal const int EscapedZeroByte = 0;
    internal const int ZeroEscapeMarker = 0xFF;
    internal const ulong SortableSignMask = 1UL << 63;
    internal const string UnknownVersion = "Unknown key codec version.";
    internal const string UnsupportedComponent = "This type is not supported by key codec v1.";
    internal const string NonfiniteNumber = "Indexed numbers must be finite.";
}

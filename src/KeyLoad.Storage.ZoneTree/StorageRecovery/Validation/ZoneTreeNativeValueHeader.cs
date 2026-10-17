using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeNativeValueHeader
{
    private const int MarkerOffset = 0;
    private const string Invalid = "A native stored record has an invalid value header.";

    internal static void RequireLive(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty || value[MarkerOffset] != LiveValueMarker)
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
    }
}

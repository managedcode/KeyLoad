using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeValueHeader
{
    private const string Invalid = "A execution stored record has an invalid value header.";

    internal static void RequireLive(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty || value[0] != LiveValueMarker)
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
    }
}

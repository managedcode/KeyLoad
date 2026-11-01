using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ProcessRecoveryTests;

/// <summary>Identifies the position record that every store commit rewrites.</summary>
/// <remarks>Model images leave this storage record out; tests assert the store position explicitly.</remarks>
internal static class StorePositionRecord
{
    private static readonly byte[] Key = KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
        ZoneTreePersistenceFormat.PositionKey);

    internal static bool Is(ReadOnlySpan<byte> key) => key.SequenceEqual(Key);
}

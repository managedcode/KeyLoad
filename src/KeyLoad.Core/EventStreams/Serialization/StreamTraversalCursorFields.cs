namespace KeyLoad.Core;

internal static class StreamTraversalCursorFields
{
    internal const uint Version = 0;
    internal const uint Purpose = 1;
    internal const uint Incarnation = 2;
    internal const uint Stream = 3;
    internal const uint Principal = 4;
    internal const uint PolicyEpoch = 5;
    internal const uint SchemaVersion = 6;
    internal const uint Direction = 8;
    internal const uint Tail = 9;
    internal const uint FirstAvailable = 10;
    internal const uint SnapshotCut = 11;
    internal const uint NextExclusive = 12;
    internal const uint ExpiresAt = 13;
}

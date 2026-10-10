namespace KeyLoad.Storage.ZoneTree;

internal readonly record struct ZoneTreeCapturedReadRecord(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value);

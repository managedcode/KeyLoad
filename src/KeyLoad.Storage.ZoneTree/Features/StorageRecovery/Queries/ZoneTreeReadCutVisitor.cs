namespace KeyLoad.Storage.ZoneTree;

internal delegate bool ZoneTreeReadCutVisitor(ReadOnlySpan<byte> key, ReadOnlySpan<byte> logicalValue);

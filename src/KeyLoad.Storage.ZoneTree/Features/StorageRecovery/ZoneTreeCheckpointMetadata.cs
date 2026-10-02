namespace KeyLoad.Storage.ZoneTree;

internal sealed record ZoneTreeCheckpointMetadata(
    int Version,
    int CodecVersion,
    Guid Incarnation,
    long Position,
    long AppliedPosition);

internal sealed record ZoneTreeCheckpointFooter(long Records, string Checksum);

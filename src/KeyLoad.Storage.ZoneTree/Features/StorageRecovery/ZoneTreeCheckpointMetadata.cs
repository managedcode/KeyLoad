namespace KeyLoad.Storage.ZoneTree;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ZoneTreeMetadataAliases.CheckpointMetadata)]
internal sealed record ZoneTreeCheckpointMetadata(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] int CodecVersion,
    [property: global::Orleans.Id(2)] Guid Incarnation,
    [property: global::Orleans.Id(3)] long Position,
    [property: global::Orleans.Id(4)] long AppliedPosition);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ZoneTreeMetadataAliases.CheckpointFooter)]
internal sealed record ZoneTreeCheckpointFooter(
    [property: global::Orleans.Id(0)] long Records,
    [property: global::Orleans.Id(1)] string Checksum);

namespace KeyLoad.Storage.ZoneTree;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ZoneTreeMetadataAliases.CheckpointFooter)]
internal sealed record ZoneTreeCheckpointFooter(
    [property: global::Orleans.Id(0)] long Records,
    [property: global::Orleans.Id(1)] string Checksum);

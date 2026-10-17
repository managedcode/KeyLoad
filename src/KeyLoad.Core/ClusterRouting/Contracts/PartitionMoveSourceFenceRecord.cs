namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.SourceFenceAlias)]
internal sealed record PartitionMoveSourceFenceRecord(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid MoveId,
    [property: Orleans.Id(2)] PartitionRef Partition,
    [property: Orleans.Id(3)] PhysicalShardRecord ControlOwner,
    [property: Orleans.Id(4)] AtomicPartitionPlacementResolution SourcePlacement,
    [property: Orleans.Id(5)] PhysicalShardRecord DestinationOwner,
    [property: Orleans.Id(6)] long SourceCut,
    [property: Orleans.Id(7)] string ControlIntentDigest);

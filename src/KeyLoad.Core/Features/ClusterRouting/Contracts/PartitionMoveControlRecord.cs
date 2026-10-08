namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.ControlAlias)]
internal sealed record PartitionMoveControlRecord(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid MoveId,
    [property: Orleans.Id(2)] PartitionRef Partition,
    [property: Orleans.Id(3)] string PrincipalId,
    [property: Orleans.Id(4)] long PolicyEpoch,
    [property: Orleans.Id(5)] AtomicPartitionPlacementResolution SourcePlacement,
    [property: Orleans.Id(6)] PhysicalShardRecord DestinationOwner,
    [property: Orleans.Id(7)] PartitionMovePhase Phase,
    [property: Orleans.Id(8)] long SourceCut,
    [property: Orleans.Id(9)] long ControlPosition,
    [property: Orleans.Id(10)] string? ImageDigest,
    [property: Orleans.Id(11)] CommitToken? InstalledReceipt,
    [property: Orleans.Id(12)] AtomicPartitionPlacementV1? PublishedPlacement);

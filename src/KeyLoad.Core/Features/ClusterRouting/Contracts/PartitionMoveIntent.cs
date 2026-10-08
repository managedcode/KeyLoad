namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.IntentAlias)]
internal sealed record PartitionMoveIntent(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid MoveId,
    [property: Orleans.Id(2)] PartitionRef Partition,
    [property: Orleans.Id(3)] string PrincipalId,
    [property: Orleans.Id(4)] long PolicyEpoch,
    [property: Orleans.Id(5)] AtomicPartitionPlacementResolution SourcePlacement,
    [property: Orleans.Id(6)] PhysicalShardRecord DestinationOwner,
    [property: Orleans.Id(7)] long ControlPosition);

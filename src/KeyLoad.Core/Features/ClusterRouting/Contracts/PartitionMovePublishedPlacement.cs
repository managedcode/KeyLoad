namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.PublishedPlacementAlias)]
internal sealed record PartitionMovePublishedPlacement(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid MoveId,
    [property: Orleans.Id(2)] AtomicPartitionPlacementV1 Placement,
    [property: Orleans.Id(3)] AtomicPartitionPlacementResolution Source,
    [property: Orleans.Id(4)] PhysicalShardRecord Destination,
    [property: Orleans.Id(5)] CommitToken Installed,
    [property: Orleans.Id(6)] PartitionMoveJournalReceipt ControlFinalize);

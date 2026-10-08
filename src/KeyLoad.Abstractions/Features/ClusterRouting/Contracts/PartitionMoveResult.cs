namespace KeyLoad;

/// <summary>Reports only a durable phase; an installed image alone never grants target write authority.</summary>
/// <param name="MoveId">The exact admitted movement identity.</param>
/// <param name="Partition">The unchanged logical partition scope.</param>
/// <param name="Phase">The last settled durable movement phase.</param>
/// <param name="SourceOwner">Original committed physical owner.</param>
/// <param name="DestinationOwner">Server-confirmed destination physical owner.</param>
/// <param name="SourceCut">Original fenced canonical source commit position.</param>
/// <param name="InstalledReceipt">Actual destination installation commit token, absent until installed.</param>
/// <param name="PublishedPlacement">Actual committed control placement, absent before publication.</param>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveAliases.Result)]
public sealed record PartitionMoveResult(
    [property: Orleans.Id(0)] Guid MoveId,
    [property: Orleans.Id(1)] PartitionRef Partition,
    [property: Orleans.Id(2)] PartitionMovePhase Phase,
    [property: Orleans.Id(3)] PhysicalShardRecord SourceOwner,
    [property: Orleans.Id(4)] PhysicalShardRecord DestinationOwner,
    [property: Orleans.Id(5)] long SourceCut,
    [property: Orleans.Id(6)] CommitToken? InstalledReceipt,
    [property: Orleans.Id(7)] AtomicPartitionPlacementV1? PublishedPlacement);

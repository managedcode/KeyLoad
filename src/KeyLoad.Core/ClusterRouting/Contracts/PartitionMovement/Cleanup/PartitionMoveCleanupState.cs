namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CleanupStateAlias)]
internal sealed record PartitionMoveCleanupState(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid MoveId,
    [property: Orleans.Id(2)] PartitionRef Partition,
    [property: Orleans.Id(3)] string ControlIntentDigest,
    [property: Orleans.Id(4)] PartitionMovePeerStage Stage,
    [property: Orleans.Id(5)] PartitionMoveCleanupRole Role,
    [property: Orleans.Id(6)] int NextFamily,
    [property: Orleans.Id(7)] PartitionMoveJournalReceipt? Completion,
    [property: Orleans.Id(8)] int NextBatch);

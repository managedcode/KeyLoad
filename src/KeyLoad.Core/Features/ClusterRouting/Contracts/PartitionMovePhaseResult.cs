namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.JournalReceiptAlias)]
internal sealed record PartitionMoveJournalReceipt(
    [property: Orleans.Id(0)] Guid CommandId,
    [property: Orleans.Id(1)] PhysicalShardRecord PhysicalOwner,
    [property: Orleans.Id(2)] long AppliedPosition,
    [property: Orleans.Id(3)] string ControlIntentDigest,
    [property: Orleans.Id(4)] string? EffectDigest = null);

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.PhaseResultAlias)]
internal sealed record PartitionMovePhaseResult(
    [property: Orleans.Id(0)] Guid MoveId,
    [property: Orleans.Id(1)] PartitionMovePeerStage Stage,
    [property: Orleans.Id(2)] PartitionMoveJournalReceipt Journal,
    [property: Orleans.Id(3)] PartitionMoveControlRecord? Control,
    [property: Orleans.Id(4)] PartitionMoveSourceFenceRecord? Fence,
    [property: Orleans.Id(5)] CommitReceipt? InstalledReceipt,
    [property: Orleans.Id(6)] AtomicPartitionPlacementV1? PublishedPlacement,
    [property: Orleans.Id(7)] PartitionMovePhaseGrant? Grant = null,
    [property: Orleans.Id(8)] PartitionMoveCleanupState? Cleanup = null,
    [property: Orleans.Id(9)] PartitionControlCommandRecord? ControlledCommand = null,
    [property: Orleans.Id(10)] PartitionControlEffectPayload? ControlledEffect = null);

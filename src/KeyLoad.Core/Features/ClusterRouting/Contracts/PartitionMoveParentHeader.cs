namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Canonical RF3 parent authority, retained for the original move replay scope.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveParentHeaderAlias)]
internal sealed record PartitionMoveParentHeader(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid MoveId,
    [property: Orleans.Id(2)] PartitionRef Partition,
    [property: Orleans.Id(3)] PartitionMoveRequest OriginalTransferRequest,
    [property: Orleans.Id(4)] string OperatorPrincipalId,
    [property: Orleans.Id(5)] long InitialPolicyEpoch,
    [property: Orleans.Id(6)] PhysicalShardRecord ControlOwner,
    [property: Orleans.Id(7)] AtomicPartitionPlacementResolution SourcePlacement,
    [property: Orleans.Id(8)] PhysicalShardRecord DestinationOwner,
    [property: Orleans.Id(9)] long Generation,
    [property: Orleans.Id(10)] int RetainedPhaseCount,
    [property: Orleans.Id(11)] long RetainedMetadataBytes,
    [property: Orleans.Id(12)] Guid? PendingOriginalPhaseCommandId,
    [property: Orleans.Id(13)] PartitionMoveResult? TerminalResult,
    [property: Orleans.Id(14)] PartitionMoveJournalReceipt? TerminalObservationReceipt,
    [property: Orleans.Id(15)] Guid LastOriginalPhaseCommandId = default,
    [property: Orleans.Id(16)] PhysicalShardRecord? OriginalSourceOwner = null,
    [property: Orleans.Id(17)] Guid? InterruptedOriginalPhaseCommandId = null,
    [property: Orleans.Id(18)] long CleanupGeneration = PartitionMoveParentContractNames.InitialCleanupGeneration,
    [property: Orleans.Id(19)] Guid? OriginalCapturePhaseCommandId = null);

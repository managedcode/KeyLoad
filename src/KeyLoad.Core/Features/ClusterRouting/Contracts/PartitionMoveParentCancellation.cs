namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>A real native cancellation effect; never the result of the interrupted original Prepare.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveParentCancellationAlias)]
internal sealed record PartitionMoveParentCancellation(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid OriginalPhaseCommandId,
    [property: Orleans.Id(2)] PartitionMovePhaseCommand CancellationPhase,
    [property: Orleans.Id(3)] PartitionMoveJournalReceipt CancellationReceipt,
    [property: Orleans.Id(4)] long CancellationPolicyEpoch);

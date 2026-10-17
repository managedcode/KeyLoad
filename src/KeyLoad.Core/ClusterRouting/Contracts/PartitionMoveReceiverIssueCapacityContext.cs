namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Transaction-produced input to pure full-wrapper size admission; carries no future proof authority.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveReceiverIssueCapacityContextAlias)]
internal sealed record PartitionMoveReceiverIssueCapacityContext(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] PartitionMoveParentState ActualState,
    [property: Orleans.Id(2)] PartitionMovePhaseCommand IntendedPhase,
    [property: Orleans.Id(3)] PartitionMovePhaseGrant CandidateGrant,
    [property: Orleans.Id(4)] int MaxBatchBytes,
    [property: Orleans.Id(5)] int MaxPhaseRecordsPerMove,
    [property: Orleans.Id(6)] long MaxRetainedMetadataBytesPerMove);

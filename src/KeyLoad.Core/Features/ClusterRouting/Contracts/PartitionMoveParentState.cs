namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveParentStateAlias)]
internal sealed record PartitionMoveParentState(
    [property: Orleans.Id(0)] PartitionMoveParentHeader? Header,
    [property: Orleans.Id(1)] PartitionMoveParentPhase? Pending,
    [property: Orleans.Id(2)] PartitionMoveParentPhase? LastIssued,
    [property: Orleans.Id(3)] PartitionMoveParentPhase? Selected,
    [property: Orleans.Id(4)] PartitionMoveControlRecord? Control,
    [property: Orleans.Id(5)] PhysicalOwnerDirectoryV1 Directory,
    [property: Orleans.Id(6)] AtomicPartitionPlacementResolution Placement,
    [property: Orleans.Id(7)] long CurrentReadCut,
    [property: Orleans.Id(8)] PartitionMoveParentPhase? Interrupted = null,
    [property: Orleans.Id(9)] OperationResult? CancellationOutcome = null,
    [property: Orleans.Id(10)] OperationResult? SelectedOriginalOutcome = null,
    [property: Orleans.Id(11)] long CurrentOperatorPolicyEpoch = PartitionMoveParentContractNames.UnissuedPolicyEpoch,
    [property: Orleans.Id(12)] string CurrentOperatorPrincipalId = PartitionMoveParentContractNames.AbsentPrincipal);

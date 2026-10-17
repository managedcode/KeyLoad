namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Joins actual issuance row and actual native original outcome at one independently fresh authorized cut.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveReceiverIssuanceSnapshotAlias)]
internal sealed record PartitionMoveReceiverIssuanceSnapshot(
    [property: Orleans.Id(0)] PartitionMoveReceiverIssuance Issuance,
    [property: Orleans.Id(1)] OperationResult OriginalResult,
    [property: Orleans.Id(2)] long CurrentReadCut);

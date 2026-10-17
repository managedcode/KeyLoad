namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Only an actual native cancellation outcome plus its retained barrier may advance cleanup.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveRetireCancellationSnapshotAlias)]
internal sealed record PartitionMoveRetireCancellationSnapshot(
    [property: Orleans.Id(0)] PartitionMoveExpiredRetireCancellation? Cancellation,
    [property: Orleans.Id(1)] OperationResult? NativeCancellationResult,
    [property: Orleans.Id(2)] long CurrentReadCut);

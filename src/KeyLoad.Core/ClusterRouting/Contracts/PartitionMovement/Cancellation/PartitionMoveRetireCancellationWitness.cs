namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Bounded original source reply; verifying its native MAC and actual outcome precedes control observation.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveRetireCancellationWitnessAlias)]
internal sealed record PartitionMoveRetireCancellationWitness(
    [property: Orleans.Id(0)] PartitionMoveExpiredRetireCancellation Cancellation,
    [property: Orleans.Id(1)] ReadOnlyMemory<byte> OriginalReplyBytes,
    [property: Orleans.Id(2)] string OriginalReplySignature);

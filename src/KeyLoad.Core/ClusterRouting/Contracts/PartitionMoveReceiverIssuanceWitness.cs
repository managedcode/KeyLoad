namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Complete real receiver first-issuance reply under an independent native MAC purpose.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveReceiverIssuanceWitnessAlias)]
internal sealed record PartitionMoveReceiverIssuanceWitness(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid OriginalPhaseCommandId,
    [property: Orleans.Id(2)] Guid QueryNonce,
    [property: Orleans.Id(3)] ReadOnlyMemory<byte> OriginalReplyBytes,
    [property: Orleans.Id(4)] string OriginalReplySignature);

namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveOutcomeProofPurposeAlias)]
internal enum PartitionMoveOutcomeProofPurpose { EffectReply = 1, OutcomeReadReply = 2 }

/// <summary>Exact signed full native outcome under its original admitted effect or independent outcome-read MAC domain.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveOutcomeWitnessAlias)]
internal sealed record PartitionMoveAuthenticatedOutcomeWitness(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] PartitionMoveOutcomeProofPurpose Purpose,
    [property: Orleans.Id(2)] Guid OriginalPhaseCommandId,
    [property: Orleans.Id(3)] Guid ReplyNonce,
    [property: Orleans.Id(4)] ReadOnlyMemory<byte> OriginalReplyBytes,
    [property: Orleans.Id(5)] string OriginalReplySignature);

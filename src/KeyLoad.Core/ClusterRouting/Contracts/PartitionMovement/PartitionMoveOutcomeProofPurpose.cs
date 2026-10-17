namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveOutcomeProofPurposeAlias)]
internal enum PartitionMoveOutcomeProofPurpose
{
    EffectReply = 1, OutcomeReadReply = 2
}

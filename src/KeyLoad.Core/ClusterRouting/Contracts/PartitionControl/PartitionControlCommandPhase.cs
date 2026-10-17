namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CommandPhaseAlias)]
internal enum PartitionControlCommandPhase
{
    None = 0,
    Admitted = 1,
    EffectAcknowledged = 2,
    Finalized = 3,
}

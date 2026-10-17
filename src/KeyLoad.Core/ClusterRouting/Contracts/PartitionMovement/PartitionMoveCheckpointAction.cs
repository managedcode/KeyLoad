namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveCheckpointActionAlias)]
internal enum PartitionMoveCheckpointAction
{
    Admit = 1, Observe = 2, CompactTerminal = 3, CancelUnprepared = 4, ObserveCancellation = 5, ObserveRetireCancellation = 6, AdmitRetireCancellation = 7
}

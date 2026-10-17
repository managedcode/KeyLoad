namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CleanupRoleAlias)]
internal enum PartitionMoveCleanupRole
{
    Source = 1, Target = 2
}

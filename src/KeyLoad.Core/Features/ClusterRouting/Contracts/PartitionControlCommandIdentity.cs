namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CommandIdentityAlias)]
internal sealed record PartitionControlCommandIdentity(
    [property: Orleans.Id(0)] CommandOutcomeScopeKind ScopeKind,
    [property: Orleans.Id(1)] PartitionRef? Partition,
    [property: Orleans.Id(2)] string PrincipalId,
    [property: Orleans.Id(3)] Guid CommandId);

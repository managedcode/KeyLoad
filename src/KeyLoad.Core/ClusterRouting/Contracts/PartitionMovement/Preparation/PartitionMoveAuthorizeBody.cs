namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.GrantBodyAlias)]
internal sealed record PartitionMoveAuthorizeBody(
    [property: Orleans.Id(0)] Guid GrantId,
    [property: Orleans.Id(1)] Guid PhaseCommandId,
    [property: Orleans.Id(2)] string OperatorPrincipalId,
    [property: Orleans.Id(3)] PartitionMovePhaseCommand Phase,
    [property: Orleans.Id(4)] PhysicalShardRecord ReceiverOwner,
    [property: Orleans.Id(5)] DateTimeOffset ExpiresAt);

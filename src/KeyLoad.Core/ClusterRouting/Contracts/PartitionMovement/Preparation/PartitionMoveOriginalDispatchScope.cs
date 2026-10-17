namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveOriginalDispatchIdentityAlias)]
internal sealed record PartitionMoveOriginalDispatchScope(
    [property: Orleans.Id(0)] Guid PhaseCommandId,
    [property: Orleans.Id(1)] PartitionMovePhaseCommand OriginalPhase,
    [property: Orleans.Id(2)] PartitionMovePhaseGrant? OriginalGrant,
    [property: Orleans.Id(3)] DateTimeOffset OriginalExpiresAt);

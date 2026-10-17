namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CleanupBodyAlias)]
internal sealed record PartitionMoveCleanupBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionMoveControlRecord Control,
    [property: Orleans.Id(2)] PartitionMoveCleanupRole Role,
    [property: Orleans.Id(3)] int FamilyOrdinal,
    [property: Orleans.Id(4)] Guid PrecedingGrantId,
    [property: Orleans.Id(5)] PartitionMovePublishedPlacement? Publication = null);

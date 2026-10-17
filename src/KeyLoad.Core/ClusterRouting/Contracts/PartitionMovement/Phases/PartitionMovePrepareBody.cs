namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.PrepareBodyAlias)]
internal sealed record PartitionMovePrepareBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionMoveRequest Request,
    [property: Orleans.Id(2)] bool RequireParentCheckpoint = false);

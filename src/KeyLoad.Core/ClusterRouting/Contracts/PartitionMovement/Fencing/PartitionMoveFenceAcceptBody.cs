namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.FenceAcceptBodyAlias)]
internal sealed record PartitionMoveFenceAcceptBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionMoveControlRecord Control,
    [property: Orleans.Id(2)] PartitionMoveSourceFenceRecord Fence,
    [property: Orleans.Id(3)] Guid FenceGrantId);

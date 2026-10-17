namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.ControlBodyAlias)]
internal sealed record PartitionMoveControlBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionMoveControlRecord Control);

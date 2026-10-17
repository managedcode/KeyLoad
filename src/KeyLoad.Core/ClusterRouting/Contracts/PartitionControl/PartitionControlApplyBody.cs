namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CommandApplyBodyAlias)]
internal sealed record PartitionControlApplyBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionMoveControlRecord Control,
    [property: Orleans.Id(2)] PartitionControlDelegation Delegation,
    [property: Orleans.Id(3)] CommandRequest? Command);

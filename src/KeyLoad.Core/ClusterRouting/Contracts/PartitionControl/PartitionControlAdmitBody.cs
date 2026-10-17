namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CommandAdmitBodyAlias)]
internal sealed record PartitionControlAdmitBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionMoveControlRecord Control,
    [property: Orleans.Id(2)] ReplicatedOperation OriginalOperation,
    [property: Orleans.Id(3)] Guid EffectId,
    [property: Orleans.Id(4)] DateTimeOffset ExpiresAt);

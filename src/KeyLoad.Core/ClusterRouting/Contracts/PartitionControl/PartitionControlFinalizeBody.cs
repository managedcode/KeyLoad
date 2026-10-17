namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CommandFinalizeBodyAlias)]
internal sealed record PartitionControlFinalizeBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionControlCommandIdentity Identity,
    [property: Orleans.Id(2)] Guid EffectId);

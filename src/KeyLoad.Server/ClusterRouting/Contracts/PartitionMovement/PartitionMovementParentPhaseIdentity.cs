namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementProtocol.ParentPhaseIdentityAlias)]
internal sealed record PartitionMovementParentPhaseIdentity(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] PartitionMoveRequest OriginalTransferRequest,
    [property: global::Orleans.Id(2)] string OperatorPrincipalId,
    [property: global::Orleans.Id(3)] PartitionMovementParentPhaseRole Role,
    [property: global::Orleans.Id(4)] int Ordinal,
    [property: global::Orleans.Id(5)] long CleanupGeneration = PartitionMovementProtocol.InitialCleanupGeneration);

namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.DelegationAlias)]
internal sealed record PartitionControlDelegation(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] PartitionControlCommandIdentity Identity,
    [property: Orleans.Id(2)] string Fingerprint,
    [property: Orleans.Id(3)] Guid EffectId,
    [property: Orleans.Id(4)] Guid MoveId,
    [property: Orleans.Id(5)] AtomicPartitionPlacementResolution TargetPlacement,
    [property: Orleans.Id(6)] PrincipalRecord Principal,
    [property: Orleans.Id(7)] DateTimeOffset ExpiresAt,
    [property: Orleans.Id(8)] long ControlAdmissionPosition,
    [property: Orleans.Id(9)] System.Collections.Immutable.ImmutableArray<ResourceDefinition> Resources = default,
    [property: Orleans.Id(10)] PhysicalShardRecord? ControlOwner = null,
    [property: Orleans.Id(11)] OperationKind OriginalKind = OperationKind.Batch,
    [property: Orleans.Id(12)] string? OriginalPayloadJson = null,
    [property: Orleans.Id(13)] ReplicatedOperation? OriginalOperation = null);

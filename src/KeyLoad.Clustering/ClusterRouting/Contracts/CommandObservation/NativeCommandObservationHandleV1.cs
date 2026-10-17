namespace KeyLoad.Clustering;

/// <summary>Opaque database-signed identity for one pre-effect, original target-owned observation.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(GrainNativeContracts.CommandObservationHandleAlias)]
internal sealed record NativeCommandObservationHandleV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] string Purpose,
    [property: Orleans.Id(2)] Guid ObservationId,
    [property: Orleans.Id(3)] Guid RequestId,
    [property: Orleans.Id(4)] Guid CommandId,
    [property: Orleans.Id(5)] OperationKind Kind,
    [property: Orleans.Id(6)] string PrincipalId,
    [property: Orleans.Id(7)] Guid Incarnation,
    [property: Orleans.Id(8)] PhysicalShardRecord PhysicalOwner,
    [property: Orleans.Id(9)] ReplicaSiloDiscovery Runtime,
    [property: Orleans.Id(10)] Guid NodeId,
    [property: Orleans.Id(11)] long ReadGeneration,
    [property: Orleans.Id(12)] DateTimeOffset ExpiresAt,
    [property: Orleans.Id(13)] ReadOnlyMemory<byte> CommandDigest);

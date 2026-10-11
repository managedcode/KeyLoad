namespace KeyLoad.Core;

[Orleans.GenerateSerializer, Orleans.Alias(EventVectorFirstIssuance.SerializerAlias)]
internal sealed record EventVectorFirstIssuance(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] string PrincipalId,
    [property: Orleans.Id(2)] Guid OriginalPublicCommandId,
    [property: Orleans.Id(3)] PartitionRef ControlPartition,
    [property: Orleans.Id(4)] Guid MapId,
    [property: Orleans.Id(5)] ReadOnlyMemory<byte> OriginalPublicRequestBytes,
    [property: Orleans.Id(6)] ReadOnlyMemory<byte> OriginalPublicRequestDigest,
    [property: Orleans.Id(7)] DateTimeOffset OriginalParentExpiresAt,
    [property: Orleans.Id(8)] System.Collections.Immutable.ImmutableArray<EventVectorAuthorizationScope> OriginalAuthorizedSourceScopes,
    [property: Orleans.Id(9)] ReadOnlyMemory<byte> OriginalCoverageWitnessBytes,
    [property: Orleans.Id(10)] ReadOnlyMemory<byte> OriginalCapturedFirstOpenPhaseBytes,
    [property: Orleans.Id(11)] EventVectorFirstIssuanceState State,
    [property: Orleans.Id(12)] Guid LastCleanupNativeCommandId,
    [property: Orleans.Id(13)] ReadOnlyMemory<byte> LastCleanupNativePayloadBytes,
    [property: Orleans.Id(14)] string? LastCleanupNativeFingerprint)
{
    internal const int ParentRequestField = 15;
    [Orleans.Id(ParentRequestField)]
    public Guid OriginalParentRequestId { get; init; }

    internal const string SerializerAlias = "keyload.core.event-vector-first-issuance.v1";
}

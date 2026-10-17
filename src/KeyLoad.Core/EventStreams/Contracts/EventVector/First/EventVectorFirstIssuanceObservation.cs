namespace KeyLoad.Core;

[Orleans.GenerateSerializer, Orleans.Alias(EventVectorFirstIssuanceObservation.SerializerAlias)]
internal sealed record EventVectorFirstIssuanceObservation(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] string PrincipalId,
    [property: Orleans.Id(2)] Guid OriginalPublicCommandId,
    [property: Orleans.Id(3)] PhysicalShardRecord ControlOwner,
    [property: Orleans.Id(4)] Guid NodeId,
    [property: Orleans.Id(5)] long ReadGeneration,
    [property: Orleans.Id(6)] Guid OriginalAdmitCommandId,
    [property: Orleans.Id(7)] ReadOnlyMemory<byte> OriginalAdmitStoredOutcomeBytes,
    [property: Orleans.Id(8)] ReadOnlyMemory<byte> ActiveIssuanceRowBytes,
    [property: Orleans.Id(9)] ReadOnlyMemory<byte> CurrentMapRowBytes,
    [property: Orleans.Id(10)] ReadOnlyMemory<byte> OriginalParentCallRowBytes,
    [property: Orleans.Id(11)] ReadOnlyMemory<byte> OriginalParentOutcomeIndexRowBytes,
    [property: Orleans.Id(12)] ReadOnlyMemory<byte> AbandonedChargedIssuanceRowBytes,
    [property: Orleans.Id(13)] ReadOnlyMemory<byte> TerminalIssuanceRowBytes,
    [property: Orleans.Id(14)] ReadOnlyMemory<byte> OriginalFirstOpenStoredOutcomeBytes,
    [property: Orleans.Id(15)] ReadOnlyMemory<byte> ActualControlCapacityRowBytes)
{
    internal const string SerializerAlias = "keyload.core.event-vector-first-issuance-observation.v1";
}

namespace KeyLoad.Core;

[Orleans.GenerateSerializer, Orleans.Alias(EventVectorFirstIssuanceIdentity.SerializerAlias)]
internal sealed record EventVectorFirstIssuanceIdentity(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] EventVectorControlAction Action,
    [property: Orleans.Id(2)] string PrincipalId,
    [property: Orleans.Id(3)] Guid OriginalPublicCommandId,
    [property: Orleans.Id(4)] PartitionRef ControlPartition,
    [property: Orleans.Id(5)] ReadOnlyMemory<byte> OriginalPublicRequestDigest,
    [property: Orleans.Id(6)] ReadOnlyMemory<byte> OriginalCapturedFirstPhaseDigest)
{
    internal const string SerializerAlias = "keyload.core.event-vector-first-issuance-identity.v1";
}

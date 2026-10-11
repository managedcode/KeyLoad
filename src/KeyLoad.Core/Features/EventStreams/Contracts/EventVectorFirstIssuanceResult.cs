namespace KeyLoad.Core;

[Orleans.GenerateSerializer, Orleans.Alias(EventVectorFirstIssuanceResult.SerializerAlias)]
internal sealed record EventVectorFirstIssuanceResult(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid NativeCommandId,
    [property: Orleans.Id(2)] EventVectorFirstIssuance Issuance,
    [property: Orleans.Id(3)] CommitReceipt Receipt)
{
    [Orleans.Id(4)]
    public ReadOnlyMemory<byte> OriginalPublicResultBytes { get; init; }

    internal const string SerializerAlias = "keyload.core.event-vector-first-issuance-result.v1";
}

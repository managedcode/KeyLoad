namespace KeyLoad.Core;

[Orleans.GenerateSerializer, Orleans.Alias(EventVectorFirstIssuanceReadRequest.SerializerAlias)]
internal sealed record EventVectorFirstIssuanceReadRequest(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] EventFeedControlRequest OriginalPublicRequest,
    [property: Orleans.Id(2)] PhysicalShardRecord ControlOwner)
{
    internal const string SerializerAlias = "keyload.core.event-vector-first-issuance-read.v1";
}

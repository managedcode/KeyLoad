using KeyLoad.Core;

namespace KeyLoad.Server.Features.EventStreams;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(EventVectorFirstLookupCall.SerializerAlias)]
internal sealed record EventVectorFirstLookupCall(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] string PrincipalId,
    [property: global::Orleans.Id(2)] EventVectorFirstIssuanceReadRequest OriginalFirstIssuanceRequest)
{
    internal const string SerializerAlias = "keyload.server.event-vector-first-issuance-lookup-call.v1";
}

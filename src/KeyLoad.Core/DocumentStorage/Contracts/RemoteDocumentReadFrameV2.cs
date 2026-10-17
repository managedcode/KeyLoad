using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.DocumentStorage;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteDocumentReadFrameV2.SerializationAlias)]
internal sealed record RemoteDocumentReadFrameV2(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid QueryId,
    [property: global::Orleans.Id(2)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(3)] EntityRef Reference,
    [property: global::Orleans.Id(4)] RemoteDocumentReadFenceV1 Route,
    [property: global::Orleans.Id(5)] PrincipalRecord Principal,
    [property: global::Orleans.Id(6)] ResourceDefinition Resource,
    [property: global::Orleans.Id(7)] ResourceLifetimeCoverageV1 Coverage)
{
    internal const string SerializationAlias = "keyload.core.document-read-frame.v2";
}

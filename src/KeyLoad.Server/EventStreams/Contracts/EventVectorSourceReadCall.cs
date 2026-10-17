using KeyLoad.Core;

namespace KeyLoad.Server.Features.EventStreams;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(EventVectorSourceReadCall.SerializerAlias)]
internal sealed record EventVectorSourceReadCall(
    [property: global::Orleans.Id(EventVectorSourceReadCall.VersionField)] int Version,
    [property: global::Orleans.Id(EventVectorSourceReadCall.PrincipalField)] string PrincipalId,
    [property: global::Orleans.Id(EventVectorSourceReadCall.ReadRequestField)] EventVectorSourcesReadRequest OriginalReadRequest)
{
    internal const string SerializerAlias = "keyload.server.event-vector-source-read-call.v1";
    internal const int VersionField = 0;
    internal const int PrincipalField = 1;
    internal const int ReadRequestField = 2;
}

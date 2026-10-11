namespace KeyLoad.Server.Features.EventStreams;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(EventVectorCoverageCall.SerializerAlias)]
internal sealed record EventVectorCoverageCall(
    [property: global::Orleans.Id(EventVectorCoverageCall.VersionField)] int Version,
    [property: global::Orleans.Id(EventVectorCoverageCall.RequestField)] EventFeedControlRequest OriginalRequest,
    [property: global::Orleans.Id(EventVectorCoverageCall.PrincipalField)] string PrincipalId,
    [property: global::Orleans.Id(EventVectorCoverageCall.GroupField)] int GroupOrdinal)
{
    internal const string SerializerAlias = "keyload.server.event-vector-coverage-call.v1";
    internal const int VersionField = 0;
    internal const int RequestField = 1;
    internal const int PrincipalField = 2;
    internal const int GroupField = 3;
}

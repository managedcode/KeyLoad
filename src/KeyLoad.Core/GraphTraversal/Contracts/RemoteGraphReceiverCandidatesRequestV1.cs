namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphReceiverCandidatesRequestV1.Alias)]
internal sealed record RemoteGraphReceiverCandidatesRequestV1(
    [property: global::Orleans.Id(0)] EntityRef Target,
    [property: global::Orleans.Id(1)] string Graph,
    [property: global::Orleans.Id(2)] int Limit)
{
    internal const string Alias = "keyload.core.remote-graph.receivercandidatesrequest.v1";
}

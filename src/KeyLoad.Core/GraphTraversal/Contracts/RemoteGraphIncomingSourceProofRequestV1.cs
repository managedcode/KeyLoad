namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphIncomingSourceProofRequestV1.Alias)]
internal sealed record RemoteGraphIncomingSourceProofRequestV1(
    [property: global::Orleans.Id(0)] RemoteGraphIntentV1 Intent)
{
    internal const string Alias = "keyload.core.remote-graph.incomingsourceproofrequest.v1";
}

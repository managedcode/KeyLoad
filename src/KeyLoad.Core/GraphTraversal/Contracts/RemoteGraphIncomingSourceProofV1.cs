namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphIncomingSourceProofV1.Alias)]
internal sealed record RemoteGraphIncomingSourceProofV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] RemoteGraphIntentV1 Intent,
    [property: global::Orleans.Id(2)] EdgeRecord? Edge,
    [property: global::Orleans.Id(3)] RemoteGraphOwnerCutV1 SourceCut)
{
    internal const string Alias = "keyload.core.remote-graph.incomingsourceproof.v1";
}

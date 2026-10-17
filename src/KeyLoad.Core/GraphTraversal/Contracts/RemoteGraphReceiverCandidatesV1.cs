using System.Collections.Immutable;

namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphReceiverCandidatesV1.Alias)]
internal sealed record RemoteGraphReceiverCandidatesV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] RemoteGraphReceiverCandidatesRequestV1 Request,
    [property: global::Orleans.Id(2)] ImmutableArray<RemoteGraphReceiverStateV1> States,
    [property: global::Orleans.Id(3)] RemoteGraphOwnerCutV1 TargetCut)
{
    internal const string Alias = "keyload.core.remote-graph.receivercandidates.v1";
}

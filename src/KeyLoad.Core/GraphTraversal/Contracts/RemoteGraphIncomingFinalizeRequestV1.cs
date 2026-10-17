using System.Collections.Immutable;

namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphIncomingFinalizeRequestV1.Alias)]
internal sealed record RemoteGraphIncomingFinalizeRequestV1(
    [property: global::Orleans.Id(0)] RemoteGraphReceiverCandidatesV1 Candidates,
    [property: global::Orleans.Id(1)] ImmutableArray<RemoteGraphIncomingSourceProofV1> Proofs)
{
    internal const string Alias = "keyload.core.remote-graph.incomingfinalizerequest.v1";
}

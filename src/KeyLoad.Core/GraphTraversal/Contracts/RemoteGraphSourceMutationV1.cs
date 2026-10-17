using System.Collections.Immutable;

namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphSourceMutationV1.Alias)]
internal sealed record RemoteGraphSourceMutationV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] RemoteGraphSourceIdentityV1 Original,
    [property: global::Orleans.Id(2)] long ExpectedOwnerRevision,
    [property: global::Orleans.Id(3)] ImmutableArray<RemoteGraphVerifiedPreparationV1> Preparations,
    [property: global::Orleans.Id(4)] EdgeRecord? PreviousEdge)
{
    internal const string Alias = "keyload.core.remote-graph.sourcemutation.v1";
}

using System.Collections.Immutable;

namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphSourceSnapshotV1.Alias)]
internal sealed record RemoteGraphSourceSnapshotV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] RemoteGraphSourceReadRequestV1 Request,
    [property: global::Orleans.Id(2)] EdgeRecord? Edge,
    [property: global::Orleans.Id(3)] long OwnerRevision,
    [property: global::Orleans.Id(4)] ImmutableArray<RemoteGraphIntentV1> Intents,
    [property: global::Orleans.Id(5)] RemoteGraphOwnerCutV1 Cut,
    [property: global::Orleans.Id(6)] StoredOutcome? OriginalOutcome)
{
    internal const string Alias = "keyload.core.remote-graph.source-snapshot.v1";
}

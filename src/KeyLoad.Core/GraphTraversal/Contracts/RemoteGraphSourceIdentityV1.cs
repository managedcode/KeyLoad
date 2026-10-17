namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphSourceIdentityV1.Alias)]
internal sealed record RemoteGraphSourceIdentityV1(
    [property: global::Orleans.Id(0)] Guid CommandId,
    [property: global::Orleans.Id(1)] PartitionRef SourcePartition,
    [property: global::Orleans.Id(2)] UpsertEdge? Upsert,
    [property: global::Orleans.Id(3)] DeleteEdge? Delete)
{
    internal const string Alias = "keyload.core.remote-graph.sourceidentity.v1";
}

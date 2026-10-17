namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphSourceReadRequestV1.Alias)]
internal sealed record RemoteGraphSourceReadRequestV1(
    [property: global::Orleans.Id(0)] Guid OriginalCommandId,
    [property: global::Orleans.Id(1)] PartitionRef SourcePartition,
    [property: global::Orleans.Id(2)] string Graph,
    [property: global::Orleans.Id(3)] string EdgeId,
    [property: global::Orleans.Id(4)] PhysicalShardRecord SourceOwner,
    [property: global::Orleans.Id(5)] RemoteGraphSourceIdentityV1? Original,
    [property: global::Orleans.Id(6)] DeliverRemoteGraphEdgeRequestV1? Delivery = null)
{
    internal const string Alias = "keyload.core.remote-graph.source-read-request.v1";
}

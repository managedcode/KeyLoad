namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphCapacityV1.Alias)]
internal sealed record RemoteGraphCapacityV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] string Direction,
    [property: global::Orleans.Id(2)] long RecordCount,
    [property: global::Orleans.Id(3)] long EncodedBytes)
{
    internal const string Alias = "keyload.core.remote-graph.capacity.v1";
}

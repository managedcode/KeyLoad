namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphParentRouteV1.Alias)]
internal sealed record RemoteGraphParentRouteV1(
    [property: global::Orleans.Id(0)] RegisteredPhysicalOwnerV1 Source,
    [property: global::Orleans.Id(1)] RegisteredPhysicalOwnerV1? Destination,
    [property: global::Orleans.Id(2)] PhysicalOwnerDirectoryV1 Directory)
{
    internal const string Alias = "keyload.core.remote-graph.parentroute.v1";
}

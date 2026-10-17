namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphIncomingRouteV1.Alias)]
internal sealed record RemoteGraphIncomingRouteV1(
    [property: global::Orleans.Id(0)] RegisteredPhysicalOwnerV1 Target,
    [property: global::Orleans.Id(1)] PhysicalOwnerDirectoryV1 Directory)
{
    internal const string Alias = "keyload.core.remote-graph.incomingroute.v1";
}

namespace KeyLoad;

/// <summary>Remote incoming graph edge row.</summary>
/// <param name="Edge">Edge.</param>
/// <param name="DeliveredRevision">Delivered revision.</param>
/// <param name="SourceCut">Source cut.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteIncomingGraphEdgeRowV1.Alias)]
public sealed record RemoteIncomingGraphEdgeRowV1(
    [property: global::Orleans.Id(0)] EdgeRecord Edge,
    [property: global::Orleans.Id(1)] long DeliveredRevision,
    [property: global::Orleans.Id(2)] RemoteGraphOwnerCutV1 SourceCut)
{
    internal const string Alias = "keyload.contract.remote-graph-incoming-row.v1";
}

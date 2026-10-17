using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Remote incoming graph edges page.</summary>
/// <param name="Version">Version.</param>
/// <param name="Rows">Rows.</param>
/// <param name="TargetCut">Target cut.</param>
/// <param name="Projection">Projection.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteIncomingGraphEdgesPageV1.Alias)]
public sealed record RemoteIncomingGraphEdgesPageV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] ImmutableArray<RemoteIncomingGraphEdgeRowV1> Rows,
    [property: global::Orleans.Id(2)] RemoteGraphOwnerCutV1 TargetCut,
    [property: global::Orleans.Id(3)] string Projection)
{
    internal const string Alias = "keyload.contract.remote-graph-incoming-page.v1";
}

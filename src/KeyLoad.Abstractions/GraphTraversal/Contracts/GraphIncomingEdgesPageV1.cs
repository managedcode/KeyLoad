using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Marks the bounded reverse projection as eventual, not complete canonical graph truth.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionAliases.IncomingPage)]
public sealed record GraphIncomingEdgesPageV1(
    [property: Orleans.Id(GraphCrossPartitionFields.PageVersion)] int Version,
    [property: Orleans.Id(GraphCrossPartitionFields.PageRows)] ImmutableArray<GraphIncomingEdgeRowV1> Rows,
    [property: Orleans.Id(GraphCrossPartitionFields.CutPosition)] long CutPosition,
    [property: Orleans.Id(GraphCrossPartitionFields.Projection)] string Projection);

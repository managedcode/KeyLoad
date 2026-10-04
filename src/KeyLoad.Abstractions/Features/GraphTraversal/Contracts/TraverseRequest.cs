using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Describes a bounded graph traversal from a starting entity.</summary>
/// <param name="Partition">The partition containing the graph.</param>
/// <param name="Graph">The graph name.</param>
/// <param name="Start">The entity where traversal begins.</param>
/// <param name="MaxDepth">The maximum traversal depth.</param>
/// <param name="MaxVertices">The maximum number of vertices to return.</param>
/// <param name="MaxEdges">The maximum number of edges to return.</param>
/// <param name="Labels">Optional edge labels used to constrain traversal.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.TraverseRequest)]
public sealed record TraverseRequest([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Graph, [property: Orleans.Id(2)] EntityRef Start, [property: Orleans.Id(3)] int MaxDepth = 3,
    [property: Orleans.Id(4)] int MaxVertices = 1_000, [property: Orleans.Id(5)] int MaxEdges = 5_000, [property: Orleans.Id(6)] ImmutableArray<string>? Labels = null);

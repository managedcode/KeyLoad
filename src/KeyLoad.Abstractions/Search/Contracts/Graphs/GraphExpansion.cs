using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Expands selected search hits into bounded related-document context.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphSearchContractAliases.GraphExpansion)]
public sealed record GraphExpansion(
    [property: Orleans.Id(0)] string Graph,
    [property: Orleans.Id(1)] int MaxDepth = GraphExpansion.DefaultMaxDepth,
    [property: Orleans.Id(2)] int MaxVertices = GraphExpansion.DefaultMaxVertices,
    [property: Orleans.Id(3)] int MaxEdges = GraphExpansion.DefaultMaxEdges,
    [property: Orleans.Id(4)] ImmutableArray<string>? Labels = null)
{
    private const int DefaultMaxDepth = 1;
    private const int DefaultMaxVertices = 1_000;
    private const int DefaultMaxEdges = 5_000;
}

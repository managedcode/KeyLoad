using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Describes one bounded outgoing graph walk from explicit entity seeds.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphSearchContractAliases.GraphWalkSpec)]
public sealed record GraphWalkSpec(
    [property: Orleans.Id(0)] string Graph,
    [property: Orleans.Id(1)] ImmutableArray<EntityRef> Seeds,
    [property: Orleans.Id(2)] int MaxDepth = GraphWalkSpec.DefaultMaxDepth,
    [property: Orleans.Id(3)] int MaxVertices = GraphWalkSpec.DefaultMaxVertices,
    [property: Orleans.Id(4)] int MaxEdges = GraphWalkSpec.DefaultMaxEdges,
    [property: Orleans.Id(5)] ImmutableArray<string>? Labels = null)
{
    private const int DefaultMaxDepth = 3;
    private const int DefaultMaxVertices = 1_000;
    private const int DefaultMaxEdges = 5_000;
}

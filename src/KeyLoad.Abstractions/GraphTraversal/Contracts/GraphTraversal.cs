using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Contains the vertices and edges found by a graph traversal.</summary>
/// <param name="Vertices">The visited vertices.</param>
/// <param name="Edges">The traversed edges.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.GraphTraversal)]
public sealed record GraphTraversal([property: Orleans.Id(0)] ImmutableArray<EntityRef> Vertices, [property: Orleans.Id(1)] ImmutableArray<EdgeRecord> Edges)
{
    /// <summary>Gets the distinct visible vertices visited, including the starting vertex.</summary>
    [Orleans.Id(2)]
    public int VertexCount { get; init; } = Vertices.Length;
}

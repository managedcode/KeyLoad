using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Contains the vertices and edges found by a graph traversal.</summary>
/// <param name="Vertices">The visited vertices.</param>
/// <param name="Edges">The traversed edges.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.GraphTraversal)]
public sealed record GraphTraversal([property: Orleans.Id(0)] ImmutableArray<EntityRef> Vertices, [property: Orleans.Id(1)] ImmutableArray<EdgeRecord> Edges);

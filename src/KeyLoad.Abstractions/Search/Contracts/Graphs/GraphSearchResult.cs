using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Contains unchanged fused search hits and optional graph context.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphSearchContractAliases.GraphSearchResult)]
public sealed record GraphSearchResult(
    [property: Orleans.Id(0)] ImmutableArray<RankedDocument> Hits,
    [property: Orleans.Id(1)] GraphExpansionResult? Expansion = null);

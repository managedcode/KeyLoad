using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Contains separate graph context and its explicit completeness declaration.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphSearchContractAliases.GraphExpansionResult)]
public sealed record GraphExpansionResult(
    [property: Orleans.Id(0)] ImmutableArray<GraphContextDocument> Documents,
    [property: Orleans.Id(1)] GraphExpansionCompleteness Completeness = GraphExpansionCompleteness.SelectedHits);

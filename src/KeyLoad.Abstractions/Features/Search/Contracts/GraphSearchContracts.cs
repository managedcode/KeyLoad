using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Describes one bounded outgoing graph walk from explicit entity seeds.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphSearchContractAliases.GraphWalkSpec)]
public sealed record GraphWalkSpec(
    [property: Orleans.Id(0)] string Graph,
    [property: Orleans.Id(1)] ImmutableArray<EntityRef> Seeds,
    [property: Orleans.Id(2)] int MaxDepth = 3,
    [property: Orleans.Id(3)] int MaxVertices = 1_000,
    [property: Orleans.Id(4)] int MaxEdges = 5_000,
    [property: Orleans.Id(5)] ImmutableArray<string>? Labels = null);

/// <summary>Requires search hits to be reachable from the configured graph seeds.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphSearchContractAliases.GraphScope)]
public sealed record GraphScope([property: Orleans.Id(0)] GraphWalkSpec Walk);

/// <summary>Adds reachable search documents as an independent shortest-hop branch.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphSearchContractAliases.GraphRetriever)]
public sealed record GraphRetriever([property: Orleans.Id(0)] GraphWalkSpec Walk, [property: Orleans.Id(1)] double Weight = 1);

/// <summary>Expands selected search hits into bounded related-document context.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphSearchContractAliases.GraphExpansion)]
public sealed record GraphExpansion(
    [property: Orleans.Id(0)] string Graph,
    [property: Orleans.Id(1)] int MaxDepth = 1,
    [property: Orleans.Id(2)] int MaxVertices = 1_000,
    [property: Orleans.Id(3)] int MaxEdges = 5_000,
    [property: Orleans.Id(4)] ImmutableArray<string>? Labels = null);

/// <summary>Combines standard search with versioned graph scope, retrieval and expansion operators.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphSearchContractAliases.GraphSearchRequest)]
public sealed record GraphSearchRequest(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] SearchRequest Search,
    [property: Orleans.Id(2)] GraphScope? Scope = null,
    [property: Orleans.Id(3)] GraphRetriever? Retriever = null,
    [property: Orleans.Id(4)] GraphExpansion? Expansion = null);

/// <summary>Returns an authorized projected document discovered during graph expansion.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphSearchContractAliases.GraphContextDocument)]
public sealed record GraphContextDocument(
    [property: Orleans.Id(0)] DocumentResult Document,
    [property: Orleans.Id(1)] int ShortestHops);

/// <summary>Declares the candidate-limited completeness of graph expansion.</summary>
public enum GraphExpansionCompleteness
{
    /// <summary>Expansion starts only from the selected final search hits.</summary>
    SelectedHits
}

/// <summary>Contains separate graph context and its explicit completeness declaration.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphSearchContractAliases.GraphExpansionResult)]
public sealed record GraphExpansionResult(
    [property: Orleans.Id(0)] ImmutableArray<GraphContextDocument> Documents,
    [property: Orleans.Id(1)] GraphExpansionCompleteness Completeness = GraphExpansionCompleteness.SelectedHits);

/// <summary>Contains unchanged fused search hits and optional graph context.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphSearchContractAliases.GraphSearchResult)]
public sealed record GraphSearchResult(
    [property: Orleans.Id(0)] ImmutableArray<RankedDocument> Hits,
    [property: Orleans.Id(1)] GraphExpansionResult? Expansion = null);

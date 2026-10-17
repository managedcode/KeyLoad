namespace KeyLoad;

/// <summary>Combines standard search with versioned graph scope, retrieval and expansion operators.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphSearchContractAliases.GraphSearchRequest)]
public sealed record GraphSearchRequest(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] SearchRequest Search,
    [property: Orleans.Id(2)] GraphScope? Scope = null,
    [property: Orleans.Id(3)] GraphRetriever? Retriever = null,
    [property: Orleans.Id(4)] GraphExpansion? Expansion = null);

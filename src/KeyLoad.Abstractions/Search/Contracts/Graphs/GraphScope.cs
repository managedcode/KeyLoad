namespace KeyLoad;

/// <summary>Requires search hits to be reachable from the configured graph seeds.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphSearchContractAliases.GraphScope)]
public sealed record GraphScope([property: Orleans.Id(0)] GraphWalkSpec Walk);

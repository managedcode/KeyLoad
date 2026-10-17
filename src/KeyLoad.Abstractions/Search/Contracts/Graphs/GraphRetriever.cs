namespace KeyLoad;

/// <summary>Adds reachable search documents as an independent shortest-hop branch.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphSearchContractAliases.GraphRetriever)]
public sealed record GraphRetriever([property: Orleans.Id(0)] GraphWalkSpec Walk, [property: Orleans.Id(1)] double Weight = GraphRetriever.DefaultWeight)
{
    private const int DefaultWeight = 1;
}

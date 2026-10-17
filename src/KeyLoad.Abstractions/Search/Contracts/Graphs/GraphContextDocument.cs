namespace KeyLoad;

/// <summary>Returns an authorized projected document discovered during graph expansion.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphSearchContractAliases.GraphContextDocument)]
public sealed record GraphContextDocument(
    [property: Orleans.Id(0)] DocumentResult Document,
    [property: Orleans.Id(1)] int ShortestHops);

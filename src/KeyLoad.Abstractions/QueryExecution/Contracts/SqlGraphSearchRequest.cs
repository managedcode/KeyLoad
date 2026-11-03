namespace KeyLoad;

/// <summary>Contains one bounded SQL graph-search statement.</summary>
/// <param name="Query">The bounded SQL request and its named JSON parameters.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(SqlGraphSearchContractAliases.Request)]
public sealed record SqlGraphSearchRequest(
    [property: Orleans.Id(1)] QueryRequest Query);

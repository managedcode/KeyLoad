namespace KeyLoad;

/// <summary>Contains one bounded versioned SQL shortest-path statement.</summary>
/// <param name="Version">The SQL graph-path profile version.</param>
/// <param name="Query">The bounded SQL request and its named scalar parameters.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(SqlGraphPathContractAliases.Request)]
public sealed record SqlGraphPathRequest(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] QueryRequest Query);

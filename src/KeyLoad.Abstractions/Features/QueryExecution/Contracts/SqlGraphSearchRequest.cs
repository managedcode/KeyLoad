namespace KeyLoad;

/// <summary>Names the stable generated wire contract for versioned graph SEARCH.</summary>
internal static class SqlGraphSearchContractAliases
{
    internal const string Request = "keyload.contract.sql-graph-search-request.v1";
}

/// <summary>Contains one bounded versioned SQL graph-search statement.</summary>
/// <param name="Version">The SQL graph-search profile version.</param>
/// <param name="Query">The bounded SQL request and its named JSON parameters.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(SqlGraphSearchContractAliases.Request)]
public sealed record SqlGraphSearchRequest(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] QueryRequest Query);

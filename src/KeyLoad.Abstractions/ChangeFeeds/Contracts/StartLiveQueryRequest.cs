namespace KeyLoad;

/// <summary>Requests an initial bounded snapshot for a live query.</summary>
/// <param name="Query">The typed query to observe.</param>
/// <remarks>The live query profile does not use ordering or top-k semantics.</remarks>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.StartLiveQueryRequest)]
public sealed record StartLiveQueryRequest([property: Orleans.Id(0)] KeyLoad.Query.AstQueryRequest Query);

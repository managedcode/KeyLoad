using System.Text.Json;

namespace KeyLoad.Query;

/// <summary>Requests execution of a typed query AST against one partition.</summary>
/// <param name="Partition">The partition to query.</param>
/// <param name="Query">The typed query tree.</param>
/// <param name="Parameters">The named values bound to parameter operands.</param>
/// <param name="AllowFullScan">Whether the caller explicitly permits a full scan.</param>
/// <param name="Cursor">The continuation cursor for a prior page, if any.</param>
/// <param name="AstVersion">The query AST protocol version.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AstQueryRequest)]
public sealed record AstQueryRequest([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] SelectQuery Query, [property: Orleans.Id(2)] Dictionary<string, JsonElement>? Parameters = null,
    [property: Orleans.Id(3)] bool AllowFullScan = false, [property: Orleans.Id(4)] string? Cursor = null, [property: Orleans.Id(5)] int AstVersion = AstQueryRequest.DefaultAstVersion)
{
    private const int DefaultAstVersion = 1;
}

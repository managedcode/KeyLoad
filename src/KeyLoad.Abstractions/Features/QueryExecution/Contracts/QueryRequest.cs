using System.Text.Json;

namespace KeyLoad;

/// <summary>Describes a read-only query over one partition.</summary>
/// <param name="Partition">The partition to query.</param>
/// <param name="Sql">The query in the supported SQL dialect.</param>
/// <param name="Parameters">Named values bound by the query.</param>
/// <param name="AllowFullScan">Whether the caller explicitly permits a full scan.</param>
/// <param name="Cursor">The continuation cursor for a prior page, if any.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.QueryRequest)]
public sealed record QueryRequest([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Sql, [property: Orleans.Id(2)] Dictionary<string, JsonElement>? Parameters = null,
    [property: Orleans.Id(3)] bool AllowFullScan = false, [property: Orleans.Id(4)] string? Cursor = null);

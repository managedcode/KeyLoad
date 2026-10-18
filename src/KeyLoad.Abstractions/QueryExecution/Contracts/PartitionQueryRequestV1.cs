using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad;

/// <summary>Contains one bounded query over explicitly requested logical partitions.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(PartitionQueryContractAliases.Request)]
public sealed record PartitionQueryRequestV1(
    [property: Orleans.Id(PartitionQueryRequestFieldIds.Version)] int Version,
    [property: Orleans.Id(PartitionQueryRequestFieldIds.Partitions)] ImmutableArray<PartitionRef> Partitions,
    [property: Orleans.Id(PartitionQueryRequestFieldIds.Query)] SelectQuery Query,
    [property: Orleans.Id(PartitionQueryRequestFieldIds.Parameters)] Dictionary<string, JsonElement>? Parameters,
    [property: Orleans.Id(PartitionQueryRequestFieldIds.AllowFullScan)] bool AllowFullScan,
    [property: Orleans.Id(PartitionQueryRequestFieldIds.AstVersion)] int AstVersion);

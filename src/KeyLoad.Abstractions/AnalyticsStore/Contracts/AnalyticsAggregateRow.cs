using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>One complete authorized execution aggregate group.</summary>
/// <param name="Dimensions">Group dimension values.</param>
/// <param name="Metrics">Finite aggregate values indexed by selection name.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AnalyticsStoreContractAliases.Row)]
public sealed record AnalyticsAggregateRow(
    [property: Orleans.Id(0)] ImmutableDictionary<string, string> Dimensions,
    [property: Orleans.Id(1)] ImmutableDictionary<string, double> Metrics);

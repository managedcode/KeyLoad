using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Complete aggregate rows and the authorized input event count.</summary>
/// <param name="Rows">Bounded deterministic aggregate groups.</param>
/// <param name="EventCount">Number of authorized canonical input events.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AnalyticsStoreContractAliases.Result)]
public sealed record AnalyticsAggregateResult(
    [property: Orleans.Id(0)] ImmutableArray<AnalyticsAggregateRow> Rows,
    [property: Orleans.Id(1)] long EventCount);

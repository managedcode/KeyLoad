using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Supported complete finite analytics aggregation operations.</summary>
[Orleans.Alias(AnalyticsStoreContractAliases.AggregateKind)]
public enum AnalyticsAggregateKind
{
    /// <summary>Counts authorized canonical events; its metric name must be empty.</summary>
    Count = 0,
    /// <summary>Sums finite metric values.</summary>
    Sum = 1,
    /// <summary>Averages finite metric values.</summary>
    Average = 2,
    /// <summary>Returns the minimum metric value.</summary>
    Min = 3,
    /// <summary>Returns the maximum metric value.</summary>
    Max = 4
}

/// <summary>Names one aggregate result and its declared metric.</summary>
/// <param name="Name">Unique output metric name.</param>
/// <param name="Metric">Declared source metric, or empty for Count.</param>
/// <param name="Operation">Complete native aggregation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AnalyticsStoreContractAliases.Selection)]
public sealed record AnalyticsMetricSelection(
    [property: Orleans.Id(0)] string Name,
    [property: Orleans.Id(1)] string Metric,
    [property: Orleans.Id(2)] AnalyticsAggregateKind Operation);

/// <summary>Queries complete bounded native aggregates from one authorized canonical event cut.</summary>
/// <param name="Partition">Canonical atomic partition.</param>
/// <param name="Store">Independent analytics store.</param>
/// <param name="FromInclusive">Inclusive UTC event-time boundary.</param>
/// <param name="ToExclusive">Exclusive UTC event-time boundary.</param>
/// <param name="GroupBy">Declared dimension names in output order.</param>
/// <param name="Metrics">Explicit aggregate selections.</param>
/// <param name="MaxGroups">Maximum complete groups; overflow refuses the operation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AnalyticsStoreContractAliases.Aggregate)]
public sealed record AggregateAnalyticsRequest(
    [property: Orleans.Id(0)] PartitionRef Partition,
    [property: Orleans.Id(1)] string Store,
    [property: Orleans.Id(2)] DateTimeOffset FromInclusive,
    [property: Orleans.Id(3)] DateTimeOffset ToExclusive,
    [property: Orleans.Id(4)] ImmutableArray<string> GroupBy,
    [property: Orleans.Id(5)] ImmutableArray<AnalyticsMetricSelection> Metrics,
    [property: Orleans.Id(6)] int MaxGroups);

/// <summary>One complete authorized native aggregate group.</summary>
/// <param name="Dimensions">Group dimension values.</param>
/// <param name="Metrics">Finite aggregate values indexed by selection name.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AnalyticsStoreContractAliases.Row)]
public sealed record AnalyticsAggregateRow(
    [property: Orleans.Id(0)] ImmutableDictionary<string, string> Dimensions,
    [property: Orleans.Id(1)] ImmutableDictionary<string, double> Metrics);

/// <summary>Complete aggregate rows and the authorized input event count.</summary>
/// <param name="Rows">Bounded deterministic aggregate groups.</param>
/// <param name="EventCount">Number of authorized canonical input events.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AnalyticsStoreContractAliases.Result)]
public sealed record AnalyticsAggregateResult(
    [property: Orleans.Id(0)] ImmutableArray<AnalyticsAggregateRow> Rows,
    [property: Orleans.Id(1)] long EventCount);

using System.Collections.Immutable;

namespace KeyLoad;

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

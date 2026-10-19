namespace KeyLoad;

/// <summary>Names one aggregate result and its declared metric.</summary>
/// <param name="Name">Unique output metric name.</param>
/// <param name="Metric">Declared source metric, or empty for Count.</param>
/// <param name="Operation">Complete execution aggregation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AnalyticsStoreContractAliases.Selection)]
public sealed record AnalyticsMetricSelection(
    [property: Orleans.Id(0)] string Name,
    [property: Orleans.Id(1)] string Metric,
    [property: Orleans.Id(2)] AnalyticsAggregateKind Operation);

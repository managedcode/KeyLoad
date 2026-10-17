namespace KeyLoad;

/// <summary>Requests complete statistics over a half-open UTC sample range.</summary>
/// <param name="Partition">Atomic partition owning the series.</param>
/// <param name="Set">Configured time-series resource.</param>
/// <param name="SeriesId">Series identity.</param>
/// <param name="From">Inclusive timestamp beginning.</param>
/// <param name="UntilExclusive">Exclusive timestamp end, or null to include the greatest representable timestamp.</param>
/// <param name="MaxSamples">Maximum raw samples before charged overflow lookahead rejects the aggregate.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AggregateSamplesRequest)]
public sealed record AggregateSamplesRequest([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Set, [property: Orleans.Id(2)] string SeriesId,
    [property: Orleans.Id(3)] DateTimeOffset From, [property: Orleans.Id(4)] DateTimeOffset? UntilExclusive = null, [property: Orleans.Id(5)] int MaxSamples = TimeSeriesReadDefaults.MaxSamples);

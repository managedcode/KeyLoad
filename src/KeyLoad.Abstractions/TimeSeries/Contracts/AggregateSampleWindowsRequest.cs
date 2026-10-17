namespace KeyLoad;

/// <summary>Requests dense fixed-width UTC statistics anchored at the range beginning.</summary>
/// <param name="Partition">Atomic partition owning the series.</param>
/// <param name="Set">Configured time-series resource.</param>
/// <param name="SeriesId">Series identity.</param>
/// <param name="From">Inclusive timestamp and window anchor.</param>
/// <param name="UntilExclusive">Exclusive range end, or null to include the greatest representable timestamp.</param>
/// <param name="Width">Positive fixed window width.</param>
/// <param name="MaxSamples">Maximum raw samples before charged lookahead rejects the operation.</param>
/// <param name="MaxWindows">Maximum dense windows, also bounded by the server result cap.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AggregateSampleWindowsRequest)]
public sealed record AggregateSampleWindowsRequest([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Set, [property: Orleans.Id(2)] string SeriesId,
    [property: Orleans.Id(3)] DateTimeOffset From, [property: Orleans.Id(4)] DateTimeOffset? UntilExclusive, [property: Orleans.Id(5)] TimeSpan Width,
    [property: Orleans.Id(6)] int MaxSamples = TimeSeriesReadDefaults.MaxSamples, [property: Orleans.Id(7)] int MaxWindows = TimeSeriesReadDefaults.MaxWindows);

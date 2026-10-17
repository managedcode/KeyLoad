using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Defines caller defaults for bounded time-series statistics.</summary>
public static class TimeSeriesReadDefaults
{
    /// <summary>Default maximum raw samples examined before charged lookahead.</summary>
    public const int MaxSamples = 10_000;
    /// <summary>Default maximum dense windows returned by one operation.</summary>
    public const int MaxWindows = 1_000;
}

/// <summary>Selects the latest authorized sample at an optional inclusive UTC cut.</summary>
/// <param name="Partition">Atomic partition owning the series.</param>
/// <param name="Set">Configured time-series resource.</param>
/// <param name="SeriesId">Series identity.</param>
/// <param name="AtOrBefore">Inclusive timestamp cut, or null for the latest committed sample.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReadLatestSampleRequest)]
public sealed record ReadLatestSampleRequest([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Set, [property: Orleans.Id(2)] string SeriesId,
    [property: Orleans.Id(3)] DateTimeOffset? AtOrBefore = null);

/// <summary>Contains an owned projected sample, or an ordinary absent-series result.</summary>
/// <param name="Sample">Latest sample under persisted authorization, or null when absent.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.LatestSampleResult)]
public sealed record LatestSampleResult([property: Orleans.Id(0)] SampleRecord? Sample);

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

/// <summary>Contains complete raw statistics; empty extrema and average are null.</summary>
/// <param name="Count">Raw sample count, including equal-timestamp samples.</param>
/// <param name="Sum">Finite ordinal sample sum, or zero for an empty range.</param>
/// <param name="Minimum">Minimum sample value, or null when empty.</param>
/// <param name="Maximum">Maximum sample value, or null when empty.</param>
/// <param name="Average">Sum divided by raw Count, or null when empty.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SampleAggregate)]
public sealed record SampleAggregate([property: Orleans.Id(0)] long Count, [property: Orleans.Id(1)] double Sum, [property: Orleans.Id(2)] double? Minimum, [property: Orleans.Id(3)] double? Maximum, [property: Orleans.Id(4)] double? Average);

/// <summary>Contains one dense half-open UTC window and its complete statistics.</summary>
/// <param name="From">Inclusive UTC beginning.</param>
/// <param name="UntilExclusive">Exclusive UTC end, or null only after the greatest representable tick.</param>
/// <param name="Aggregate">Raw statistics for this window.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SampleAggregateWindow)]
public sealed record SampleAggregateWindow([property: Orleans.Id(0)] DateTimeOffset From, [property: Orleans.Id(1)] DateTimeOffset? UntilExclusive, [property: Orleans.Id(2)] SampleAggregate Aggregate);

/// <summary>Owns the dense ascending UTC windows returned by one complete operation.</summary>
/// <param name="Windows">Immutable windows, including empty windows, serialized as a JSON array.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SampleAggregateWindowsResult)]
public sealed record SampleAggregateWindowsResult([property: Orleans.Id(0)] ImmutableArray<SampleAggregateWindow> Windows);

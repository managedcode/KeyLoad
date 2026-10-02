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
public sealed record ReadLatestSampleRequest(PartitionRef Partition, string Set, string SeriesId,
    DateTimeOffset? AtOrBefore = null);

/// <summary>Contains an owned projected sample, or an ordinary absent-series result.</summary>
/// <param name="Sample">Latest sample under persisted authorization, or null when absent.</param>
public sealed record LatestSampleResult(SampleRecord? Sample);

/// <summary>Requests complete statistics over a half-open UTC sample range.</summary>
/// <param name="Partition">Atomic partition owning the series.</param>
/// <param name="Set">Configured time-series resource.</param>
/// <param name="SeriesId">Series identity.</param>
/// <param name="From">Inclusive timestamp beginning.</param>
/// <param name="UntilExclusive">Exclusive timestamp end, or null to include the greatest representable timestamp.</param>
/// <param name="MaxSamples">Maximum raw samples before charged overflow lookahead rejects the aggregate.</param>
public sealed record AggregateSamplesRequest(PartitionRef Partition, string Set, string SeriesId,
    DateTimeOffset From, DateTimeOffset? UntilExclusive = null, int MaxSamples = TimeSeriesReadDefaults.MaxSamples);

/// <summary>Requests dense fixed-width UTC statistics anchored at the range beginning.</summary>
/// <param name="Partition">Atomic partition owning the series.</param>
/// <param name="Set">Configured time-series resource.</param>
/// <param name="SeriesId">Series identity.</param>
/// <param name="From">Inclusive timestamp and window anchor.</param>
/// <param name="UntilExclusive">Exclusive range end, or null to include the greatest representable timestamp.</param>
/// <param name="Width">Positive fixed window width.</param>
/// <param name="MaxSamples">Maximum raw samples before charged lookahead rejects the operation.</param>
/// <param name="MaxWindows">Maximum dense windows, also bounded by the server result cap.</param>
public sealed record AggregateSampleWindowsRequest(PartitionRef Partition, string Set, string SeriesId,
    DateTimeOffset From, DateTimeOffset? UntilExclusive, TimeSpan Width,
    int MaxSamples = TimeSeriesReadDefaults.MaxSamples, int MaxWindows = TimeSeriesReadDefaults.MaxWindows);

/// <summary>Contains complete raw statistics; empty extrema and average are null.</summary>
/// <param name="Count">Raw sample count, including equal-timestamp samples.</param>
/// <param name="Sum">Finite ordinal sample sum, or zero for an empty range.</param>
/// <param name="Minimum">Minimum sample value, or null when empty.</param>
/// <param name="Maximum">Maximum sample value, or null when empty.</param>
/// <param name="Average">Sum divided by raw Count, or null when empty.</param>
public sealed record SampleAggregate(long Count, double Sum, double? Minimum, double? Maximum, double? Average);

/// <summary>Contains one dense half-open UTC window and its complete statistics.</summary>
/// <param name="From">Inclusive UTC beginning.</param>
/// <param name="UntilExclusive">Exclusive UTC end, or null only after the greatest representable tick.</param>
/// <param name="Aggregate">Raw statistics for this window.</param>
public sealed record SampleAggregateWindow(DateTimeOffset From, DateTimeOffset? UntilExclusive, SampleAggregate Aggregate);

/// <summary>Owns the dense ascending UTC windows returned by one complete operation.</summary>
/// <param name="Windows">Immutable windows, including empty windows, serialized as a JSON array.</param>
public sealed record SampleAggregateWindowsResult(ImmutableArray<SampleAggregateWindow> Windows);

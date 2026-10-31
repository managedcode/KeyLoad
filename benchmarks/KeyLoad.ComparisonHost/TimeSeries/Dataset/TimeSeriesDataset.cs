using System.Globalization;

namespace KeyLoad.ComparisonHost.TimeSeries;

/// <summary>One point of a series.</summary>
internal sealed record TimePoint(DateTimeOffset At, double Value);

/// <summary>Count, sum, minimum and maximum of the points in a time range.</summary>
internal sealed record SeriesAggregate(long Count, double Sum, double Min, double Max);

/// <summary>Count and sum of one fixed-width window, keyed by the window start.</summary>
internal sealed record SeriesWindow(DateTimeOffset Start, long Count, double Sum);

/// <summary>
/// The standard time-series dataset: 1,000 series of one point per second from 2026-01-01T00:00:00Z,
/// so 1M records give 1,000 points per series.
/// </summary>
internal sealed class TimeSeriesDataset(int records)
{
    internal const int SeriesCount = 1000;
    internal static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const string SeriesPrefix = "s";
    private const string Format = "D4";

    internal int PointsPerSeries { get; } = records / SeriesCount;

    internal static string SeriesId(int series) => SeriesPrefix + series.ToString(Format, CultureInfo.InvariantCulture);

    internal static DateTimeOffset At(int point) => Epoch.AddSeconds(point);

    /// <summary>Values have one decimal: <c>((series·31 + point·17) mod 1000) / 10</c>.</summary>
    internal static double Value(int series, int point) => ((series * 31 + point * 17) % 1000) / 10d;

    internal static TimePoint Point(int series, int point) => new(At(point), Value(series, point));

    internal static SeriesAggregate Aggregate(int series, int from, int count)
    {
        var values = Enumerable.Range(from, count).Select(point => Value(series, point)).ToArray();
        return new(values.Length, values.Sum(), values.Min(), values.Max());
    }

    internal static bool Close(double actual, double expected) => Math.Abs(actual - expected) <= 1e-6 * Math.Max(1, Math.Abs(expected));
}

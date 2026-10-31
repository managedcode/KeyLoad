using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

namespace KeyLoad.ComparisonHost.TimeSeries;

/// <summary>The only operations a time-series database implements; thread-safe, shared by all clients.</summary>
internal interface ITimeSeriesDriver : IAsyncDisposable
{
    /// <summary>Points per <see cref="AppendAsync"/> call during the load; calls never mix series.</summary>
    int BatchSize { get; }

    /// <summary>Creates the measurement, hypertable or series set.</summary>
    Task PrepareAsync(CancellationToken token);

    /// <summary>Appends points to one series; returns after the database acknowledged them.</summary>
    Task AppendAsync(string series, IReadOnlyList<TimePoint> points, CancellationToken token);

    /// <summary>Reads the points with <c>from &lt;= at &lt; until</c>, in any order.</summary>
    Task<IReadOnlyList<TimePoint>> ReadRangeAsync(string series, DateTimeOffset from, DateTimeOffset until, CancellationToken token);

    /// <summary>Server-side count, sum, min and max over <c>[from, until)</c>.</summary>
    Task<SeriesAggregate> AggregateAsync(string series, DateTimeOffset from, DateTimeOffset until, CancellationToken token);

    /// <summary>Server-side count and sum per <paramref name="width"/> window aligned to <paramref name="from"/>, over <c>[from, until)</c>.</summary>
    Task<IReadOnlyList<SeriesWindow>> WindowsAsync(string series, DateTimeOffset from, DateTimeOffset until, TimeSpan width,
        CancellationToken token);
}

/// <summary>Creates the time-series driver of the deployed database.</summary>
internal static class TimeSeriesDrivers
{
    internal static ITimeSeriesDriver Create(BenchmarkConnection connection) => connection.Database switch
    {
        BenchmarkDatabases.KeyLoad => new KeyLoadTimeSeriesDriver(connection),
        BenchmarkDatabases.Timescale => new TimescaleTimeSeriesDriver(connection),
        BenchmarkDatabases.InfluxDb => new InfluxTimeSeriesDriver(connection),
        _ => throw new NotSupportedException(BenchmarkTypes.NoDriver + BenchmarkTypes.TimeSeries + BenchmarkTypes.ForDatabase + connection.Database)
    };
}

using System.Globalization;
using KeyLoad.ComparisonHost.Deployment;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.ComparisonHost.TimeSeries;

/// <summary>
/// TimescaleDB through a pooled Npgsql data source: a <c>points (series, at, value)</c> hypertable with a
/// <c>(series, at)</c> index, one multi-row <c>INSERT ... SELECT unnest(...)</c> per batch, a range <c>SELECT</c>,
/// <c>count/sum/min/max</c> and <c>time_bucket(width, at, origin =&gt; from)</c> windows computed by the server.
/// </summary>
internal sealed class TimescaleTimeSeriesDriver(BenchmarkConnection connection) : ITimeSeriesDriver
{
    private const int Batch = 1_000;
    private const int NoCommandTimeout = 0;
    private const int AtColumn = 0, ValueColumn = 1, CountColumn = 0, SumColumn = 1, MinColumn = 2, MaxColumn = 3;
    private const int BucketColumn = 0, BucketCountColumn = 1, BucketSumColumn = 2;
    private const string Schema = """
        CREATE EXTENSION IF NOT EXISTS timescaledb;
        CREATE TABLE points (series text NOT NULL, at timestamptz NOT NULL, value double precision NOT NULL);
        SELECT create_hypertable('points', by_range('at'));
        CREATE INDEX points_series_at ON points (series, at);
        """;
    private const string Insert = "INSERT INTO points (series, at, value) SELECT $1, t.at, t.value "
        + "FROM unnest($2::timestamptz[], $3::float8[]) AS t(at, value)";
    private const string Filter = " FROM points WHERE series = $1 AND at >= $2 AND at < $3";
    private const string Range = "SELECT at, value" + Filter;
    private const string Totals = "SELECT count(*), coalesce(sum(value), 0), min(value), max(value)" + Filter;
    private const string Buckets = "SELECT time_bucket($4, at, origin => $2) AS bucket, count(*), sum(value)" + Filter
        + " GROUP BY bucket ORDER BY bucket";
    private const string WrongInsert = "TimescaleDB inserted a different number of rows for series ";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReadyLimit = TimeSpan.FromMinutes(5);
    private readonly NpgsqlDataSource source = Open(connection.RequireConnectionString());

    public int BatchSize => Batch;

    /// <summary>Waits until the server accepts connections, then creates the hypertable and its index.</summary>
    public async Task PrepareAsync(CancellationToken token)
    {
        await WaitUntilReadyAsync(token);
        await using var command = source.CreateCommand(Schema);
        _ = await command.ExecuteNonQueryAsync(token);
    }

    public async Task AppendAsync(string series, IReadOnlyList<TimePoint> points, CancellationToken token)
    {
        await using var command = source.CreateCommand(Insert);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, series);
        command.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.TimestampTz, points.Select(point => point.At.UtcDateTime).ToArray());
        command.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Double, points.Select(point => point.Value).ToArray());
        var inserted = await command.ExecuteNonQueryAsync(token);
        if (inserted != points.Count)
        {
            throw new InvalidDataException(WrongInsert + series + ": " + inserted.ToString(CultureInfo.InvariantCulture));
        }
    }

    public async Task<IReadOnlyList<TimePoint>> ReadRangeAsync(string series, DateTimeOffset from, DateTimeOffset until,
        CancellationToken token)
    {
        await using var command = Command(Range, series, from, until);
        await using var reader = await command.ExecuteReaderAsync(token);
        var points = new List<TimePoint>();
        while (await reader.ReadAsync(token))
        {
            points.Add(new(Utc(reader.GetDateTime(AtColumn)), reader.GetDouble(ValueColumn)));
        }
        return points;
    }

    public async Task<SeriesAggregate> AggregateAsync(string series, DateTimeOffset from, DateTimeOffset until,
        CancellationToken token)
    {
        await using var command = Command(Totals, series, from, until);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
        {
            throw new KeyNotFoundException(series);
        }
        return new(reader.GetInt64(CountColumn), reader.GetDouble(SumColumn), Optional(reader, MinColumn), Optional(reader, MaxColumn));
    }

    public async Task<IReadOnlyList<SeriesWindow>> WindowsAsync(string series, DateTimeOffset from, DateTimeOffset until,
        TimeSpan width, CancellationToken token)
    {
        await using var command = Command(Buckets, series, from, until);
        command.Parameters.AddWithValue(NpgsqlDbType.Interval, width);
        await using var reader = await command.ExecuteReaderAsync(token);
        var windows = new List<SeriesWindow>();
        while (await reader.ReadAsync(token))
        {
            windows.Add(new(Utc(reader.GetDateTime(BucketColumn)), reader.GetInt64(BucketCountColumn),
                reader.GetDouble(BucketSumColumn)));
        }
        return windows;
    }

    public ValueTask DisposeAsync() => source.DisposeAsync();

    /// <summary>Retries opening a connection every two seconds; after five minutes the last error propagates.</summary>
    private async Task WaitUntilReadyAsync(CancellationToken token)
    {
        var clock = TimeProvider.System;
        var started = clock.GetTimestamp();
        while (true)
        {
            try
            {
                await using var opened = await source.OpenConnectionAsync(token);
                return;
            }
            catch (NpgsqlException) when (clock.GetElapsedTime(started) < ReadyLimit && !token.IsCancellationRequested)
            {
                await Task.Delay(RetryDelay, clock, token);
            }
        }
    }

    private NpgsqlCommand Command(string sql, string series, DateTimeOffset from, DateTimeOffset until)
    {
        var command = source.CreateCommand(sql);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, series);
        command.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, from.UtcDateTime);
        command.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, until.UtcDateTime);
        return command;
    }

    private static NpgsqlDataSource Open(string connectionString)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString) { CommandTimeout = NoCommandTimeout };
        return NpgsqlDataSource.Create(settings.ConnectionString);
    }

    /// <summary>Npgsql reads <c>timestamptz</c> as a UTC <see cref="DateTime"/>.</summary>
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);

    private static double Optional(NpgsqlDataReader reader, int column)
        => reader.IsDBNull(column) ? double.NaN : reader.GetDouble(column);
}

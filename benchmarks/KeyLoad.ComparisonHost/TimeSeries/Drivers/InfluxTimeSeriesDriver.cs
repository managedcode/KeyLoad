using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.ComparisonHost.Deployment;

namespace KeyLoad.ComparisonHost.TimeSeries;

/// <summary>
/// InfluxDB 3 Core over its HTTP API: one line-protocol <c>/api/v3/write_lp</c> call per batch with
/// <c>accept_partial=false&amp;no_sync=false</c> (acknowledged after the server persisted its WAL, when the rows are already
/// queryable), and SQL through <c>/api/v3/query_sql</c>: a range <c>SELECT</c>, <c>count/sum/min/max</c> and
/// <c>date_bin</c> windows computed by the server.
/// </summary>
internal sealed class InfluxTimeSeriesDriver(BenchmarkConnection connection) : ITimeSeriesDriver
{
    private const int Batch = 1_000;
    private const string Database = "benchmark", Table = "points", SeriesTag = "series", ValueField = "value";
    private const string FloatType = "float64", JsonFormat = "json", JsonMedia = "application/json", LineMedia = "text/plain";
    private const string DatabaseKey = "db", TableKey = "table", TagsKey = "tags", FieldsKey = "fields", NameKey = "name";
    private const string TypeKey = "type", QueryKey = "q", FormatKey = "format", ParamsKey = "params";
    private const string TimeColumn = "time", CountColumn = "n", SumColumn = "total", MinColumn = "low", MaxColumn = "high";
    private const string BucketColumn = "bucket";
    private const string Filter = """ FROM points WHERE "series" = $series AND "time" >= '""";
    private const string FilterMiddle = """' AND "time" < '""";
    private const string FilterEnd = "'";
    private const string RangeSql = """SELECT "time", "value" """;
    private const string TotalsSql = """SELECT count("value") AS n, sum("value") AS total, min("value") AS low, max("value") AS high""";
    private const string BucketsStart = "SELECT date_bin(INTERVAL '";
    private const string BucketsOrigin = """ seconds', "time", TIMESTAMP '""";
    private const string BucketsEnd = """') AS bucket, count("value") AS n, sum("value") AS total""";
    private const string GroupByBucket = " GROUP BY 1 ORDER BY 1";
    private const string LiteralFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", ValueFormat = "R";
    private const string Failed = "InfluxDB request failed with status ";
    private const string Separator = ": ";
    private static readonly Uri ConfigureDatabase = new("/api/v3/configure/database", UriKind.Relative);
    private static readonly Uri ConfigureTable = new("/api/v3/configure/table", UriKind.Relative);
    private static readonly Uri Write = new("/api/v3/write_lp?db=" + Database
        + "&precision=nanosecond&accept_partial=false&no_sync=false", UriKind.Relative);
    private static readonly Uri Query = new("/api/v3/query_sql", UriKind.Relative);
    private readonly HttpClient http = connection.CreateHttpClient(0);

    public int BatchSize => Batch;

    /// <summary>Creates the database and its <c>points</c> table with the <c>series</c> tag and a float <c>value</c> field.</summary>
    public async Task PrepareAsync(CancellationToken token)
    {
        _ = await PostAsync(ConfigureDatabase, new JsonObject { [DatabaseKey] = Database }.ToJsonString(), JsonMedia, token);
        var field = new JsonObject { [NameKey] = ValueField, [TypeKey] = FloatType };
        var table = new JsonObject
        {
            [DatabaseKey] = Database,
            [TableKey] = Table,
            [TagsKey] = new JsonArray(SeriesTag),
            [FieldsKey] = new JsonArray(field)
        };
        _ = await PostAsync(ConfigureTable, table.ToJsonString(), JsonMedia, token);
    }

    public async Task AppendAsync(string series, IReadOnlyList<TimePoint> points, CancellationToken token)
    {
        var prefix = Table + ',' + SeriesTag + '=' + Escape(series) + ' ' + ValueField + '=';
        var lines = new StringBuilder();
        foreach (var point in points)
        {
            lines.Append(prefix).Append(point.Value.ToString(ValueFormat, CultureInfo.InvariantCulture)).Append(' ')
                .Append(Nanoseconds(point.At).ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        _ = await PostAsync(Write, lines.ToString(), LineMedia, token);
    }

    public async Task<IReadOnlyList<TimePoint>> ReadRangeAsync(string series, DateTimeOffset from, DateTimeOffset until,
        CancellationToken token)
        => await RowsAsync(RangeSql + Where(from, until), series,
            row => new TimePoint(Time(row, TimeColumn), row.GetProperty(ValueField).GetDouble()), token);

    public async Task<SeriesAggregate> AggregateAsync(string series, DateTimeOffset from, DateTimeOffset until,
        CancellationToken token)
    {
        var rows = await RowsAsync(TotalsSql + Where(from, until), series, row => new SeriesAggregate(
            row.GetProperty(CountColumn).GetInt64(), Number(row, SumColumn), Number(row, MinColumn), Number(row, MaxColumn)), token);
        return rows.Count == 1 ? rows[0] : throw new KeyNotFoundException(series);
    }

    /// <remarks>InfluxDB SQL interval literals here use whole seconds, so other widths are rejected.</remarks>
    public async Task<IReadOnlyList<SeriesWindow>> WindowsAsync(string series, DateTimeOffset from, DateTimeOffset until,
        TimeSpan width, CancellationToken token)
    {
        if (width <= TimeSpan.Zero || width.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, null);
        }
        var seconds = (width.Ticks / TimeSpan.TicksPerSecond).ToString(CultureInfo.InvariantCulture);
        var sql = BucketsStart + seconds + BucketsOrigin + Literal(from) + BucketsEnd + Where(from, until) + GroupByBucket;
        return await RowsAsync(sql, series, row => new SeriesWindow(Time(row, BucketColumn),
            row.GetProperty(CountColumn).GetInt64(), Number(row, SumColumn)), token);
    }

    public ValueTask DisposeAsync()
    {
        http.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<List<T>> RowsAsync<T>(string sql, string series, Func<JsonElement, T> read, CancellationToken token)
    {
        var body = new JsonObject
        {
            [DatabaseKey] = Database,
            [QueryKey] = sql,
            [FormatKey] = JsonFormat,
            [ParamsKey] = new JsonObject { [SeriesTag] = series }
        };
        var text = await PostAsync(Query, body.ToJsonString(), JsonMedia, token);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }
        using var document = JsonDocument.Parse(text);
        return [.. document.RootElement.EnumerateArray().Select(read)];
    }

    private async Task<string> PostAsync(Uri path, string content, string media, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = new StringContent(content, Encoding.UTF8, media);
        using var response = await http.SendAsync(request, token);
        var body = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(Failed + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
                + Separator + body, null, response.StatusCode);
        }
        return body;
    }

    private static string Where(DateTimeOffset from, DateTimeOffset until)
        => Filter + Literal(from) + FilterMiddle + Literal(until) + FilterEnd;

    private static string Literal(DateTimeOffset value) => value.UtcDateTime.ToString(LiteralFormat, CultureInfo.InvariantCulture);

    private static long Nanoseconds(DateTimeOffset value) => checked((value - DateTimeOffset.UnixEpoch).Ticks * TimeSpan.NanosecondsPerTick);

    /// <summary>Line protocol escapes commas, equals signs and spaces in tag values.</summary>
    private static string Escape(string tag) => tag.Replace(",", "\\,", StringComparison.Ordinal)
        .Replace("=", "\\=", StringComparison.Ordinal).Replace(" ", "\\ ", StringComparison.Ordinal);

    /// <summary>InfluxDB returns timestamps as ISO 8601 text in UTC.</summary>
    private static DateTimeOffset Time(JsonElement row, string column)
    {
        var text = row.GetProperty(column).GetString() ?? throw new KeyNotFoundException(column);
        return new(DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
            TimeSpan.Zero);
    }

    /// <summary>The JSON writer omits null aggregates, which only an empty range produces.</summary>
    private static double Number(JsonElement row, string column)
        => row.TryGetProperty(column, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : double.NaN;
}

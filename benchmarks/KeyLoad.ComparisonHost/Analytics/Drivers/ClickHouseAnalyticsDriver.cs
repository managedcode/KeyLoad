using System.Buffers;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KeyLoad.ComparisonHost.Deployment;

namespace KeyLoad.ComparisonHost.Analytics;

/// <summary>
/// ClickHouse over its HTTP interface with basic authentication: a MergeTree table ordered by <c>at</c>, one
/// <c>INSERT ... FORMAT JSONEachRow</c> per batch acknowledged with <c>async_insert=1</c>, <c>wait_for_async_insert=1</c>
/// and the <c>fsync_after_insert</c>/<c>fsync_part_directory</c> table settings, and <c>count()</c>/<c>sum(amount)</c>
/// aggregates with typed query parameters, computed by the server.
/// </summary>
internal sealed class ClickHouseAnalyticsDriver(BenchmarkConnection connection) : IAnalyticsDriver
{
    private const int Batch = 1_000;
    private const string FromParameter = "from", UntilParameter = "until", ProductParameter = "product";
    private const string IdColumn = "id", AtColumn = "at", RegionColumn = "region", ProductColumn = "product";
    private const string ChannelColumn = "channel", AmountColumn = "amount", QuantityColumn = "quantity";
    private const string CountColumn = "n", SumColumn = "total", DataKey = "data";
    private const string CreateDatabase = "CREATE DATABASE benchmark";
    private const string CreateTable = "CREATE TABLE benchmark.events (id String, at DateTime('UTC'), "
        + "region LowCardinality(String), product LowCardinality(String), channel LowCardinality(String), "
        + "amount Float64, quantity Int64) ENGINE = MergeTree ORDER BY at "
        + "SETTINGS fsync_after_insert = 1, fsync_part_directory = 1";
    private const string Insert = "INSERT INTO benchmark.events FORMAT JSONEachRow\n";
    private const string Range = " FROM benchmark.events WHERE at >= toDateTime({" + FromParameter + ":String}, 'UTC') "
        + "AND at < toDateTime({" + UntilParameter + ":String}, 'UTC')";
    private const string ByRegion = "SELECT region, count() AS n, sum(amount) AS total" + Range + " GROUP BY region FORMAT JSON";
    private const string ForProduct = "SELECT count() AS n, sum(amount) AS total" + Range
        + " AND product = {" + ProductParameter + ":String} FORMAT JSON";
    private const string QueryPath = "/?wait_end_of_query=1&output_format_json_quote_64bit_integers=0";
    private const string InsertPath = QueryPath + "&async_insert=1&wait_for_async_insert=1&async_insert_busy_timeout_ms=10";
    private const string ParameterPrefix = "&param_", Assign = "=";
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss", PlainText = "text/plain";
    private const string Failed = "ClickHouse request failed with status ", Separator = ": ";
    private const int FirstRow = 0;
    private readonly HttpClient http = connection.CreateHttpClient(0);

    private static ReadOnlySpan<byte> RowEnd => "\n"u8;

    public int BatchSize => Batch;

    public async Task PrepareAsync(CancellationToken token)
    {
        _ = await SendAsync(QueryPath, Encoding.UTF8.GetBytes(CreateDatabase), token);
        _ = await SendAsync(QueryPath, Encoding.UTF8.GetBytes(CreateTable), token);
    }

    public async Task InsertAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken token)
    {
        _ = await SendAsync(InsertPath, Rows(events), token);
    }

    public async Task<IReadOnlyList<GroupTotal>> SumByRegionAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken token)
    {
        using var result = await QueryAsync(ByRegion, Window(from, until), token);
        return [.. result.RootElement.GetProperty(DataKey).EnumerateArray().Select(row => Total(
            row.GetProperty(RegionColumn).GetString() ?? throw new KeyNotFoundException(RegionColumn), row))];
    }

    public async Task<GroupTotal> SumForProductAsync(string product, DateTimeOffset from, DateTimeOffset until, CancellationToken token)
    {
        using var result = await QueryAsync(ForProduct, Window(from, until) + Parameter(ProductParameter, product), token);
        return Total(product, result.RootElement.GetProperty(DataKey)[FirstRow]);
    }

    public ValueTask DisposeAsync()
    {
        http.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<JsonDocument> QueryAsync(string sql, string parameters, CancellationToken token)
        => JsonDocument.Parse(await SendAsync(QueryPath + parameters, Encoding.UTF8.GetBytes(sql), token));

    private async Task<string> SendAsync(string path, ReadOnlyMemory<byte> body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
        request.Content = new ReadOnlyMemoryContent(body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(PlainText) { CharSet = Encoding.UTF8.WebName };
        using var response = await http.SendAsync(request, token);
        var text = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(Failed + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
                + Separator + text, null, response.StatusCode);
        }
        return text;
    }

    /// <summary>The insert statement followed by one JSON object per line.</summary>
    private static ReadOnlyMemory<byte> Rows(IReadOnlyList<AnalyticsEvent> events)
    {
        var body = new ArrayBufferWriter<byte>();
        Encoding.UTF8.GetBytes(Insert, body);
        using var writer = new Utf8JsonWriter(body);
        foreach (var item in events)
        {
            writer.WriteStartObject();
            writer.WriteString(IdColumn, item.Id);
            writer.WriteString(AtColumn, Time(item.At));
            writer.WriteString(RegionColumn, item.Region);
            writer.WriteString(ProductColumn, item.Product);
            writer.WriteString(ChannelColumn, item.Channel);
            writer.WriteNumber(AmountColumn, item.Amount);
            writer.WriteNumber(QuantityColumn, item.Quantity);
            writer.WriteEndObject();
            writer.Flush();
            body.Write(RowEnd);
            writer.Reset(body);
        }
        return body.WrittenMemory;
    }

    private static string Window(DateTimeOffset from, DateTimeOffset until)
        => Parameter(FromParameter, Time(from)) + Parameter(UntilParameter, Time(until));

    private static string Parameter(string name, string value) => ParameterPrefix + name + Assign + Uri.EscapeDataString(value);

    private static string Time(DateTimeOffset value) => value.UtcDateTime.ToString(TimeFormat, CultureInfo.InvariantCulture);

    private static GroupTotal Total(string key, JsonElement row)
        => new(key, row.GetProperty(CountColumn).GetInt64(), row.GetProperty(SumColumn).GetDouble());
}

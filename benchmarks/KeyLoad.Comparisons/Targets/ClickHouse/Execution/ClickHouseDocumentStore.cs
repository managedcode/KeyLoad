using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal sealed class ClickHouseDocumentStore(HttpClient http, string name, IOptions<ComparisonExecutionOptions> options)
    : HttpDocumentStore(http, name, options)
{
    private const string TargetName = "ClickHouse";
    private const string Ack = "MergeTree; async_insert=1 with wait_for_async_insert=1; fsync_after_insert=1; synchronous mutations";
    private const string Auth = "isolated database; password-authenticated default user";
    private const string TableSuffix = ".documents";
    private const string JsonFormat = " FORMAT JSON";
    private const string Select = "SELECT id,body FROM ";
    private const string Predicate = " WHERE id={id:String}";
    private const string NumericPredicate = " AND number>={number:UInt32} AND number<{number:UInt32}+1";
    private const string Limit = " ORDER BY id LIMIT 2";
    private const string Version = "SELECT version() AS version FORMAT JSON";
    private const string Create = "CREATE DATABASE ";
    private const string CreateTable = "CREATE TABLE ";
    private const string Columns = " (id String,body String,number UInt32) ENGINE=MergeTree ORDER BY id SETTINGS fsync_after_insert=1,fsync_part_directory=1";
    private const string Drop = "DROP DATABASE ";
    private const string Insert = "INSERT INTO ";
    private const string Values = " FORMAT JSONEachRow\n";
    private const string Update = "ALTER TABLE ";
    private const string UpdateBody = " UPDATE body={body:String} WHERE id={id:String}";
    private const string Delete = " DELETE WHERE id={id:String}";
    private const string Page = " WHERE id>{after:String} ORDER BY id LIMIT ";
    private const string Index = " ADD INDEX session_number_idx number TYPE minmax GRANULARITY 1";
    private const string Materialize = " MATERIALIZE INDEX session_number_idx";
    private const string Settings = "?wait_end_of_query=1&mutations_sync=2&async_insert=1&wait_for_async_insert=1&async_insert_busy_timeout_ms=";
    private const string ParameterPrefix = "&param_", Equal = "=";
    private const string Media = "text/plain";
    private const string Id = "id", Number = "number", Body = "body", After = "after", Data = "data", VersionField = "version";
    private const string NonemptyReply = "ClickHouseWriteReplyRejected";
    private const int FirstRow = 0;
    // ClickHouse recommends at least 1,000 and ideally 10,000-100,000 rows per insert; the low end keeps a request near 11 MB:
    // https://clickhouse.com/docs/best-practices/selecting-an-insert-strategy
    private const int InsertRows = 10_000;
    private string Table => Name + TableSuffix;
    internal override string Target => TargetName;
    internal override string Acknowledgement => Ack;
    internal override string Authorization => Auth;
    internal override bool SupportsDelete => true;
    internal override int BatchRecords => InsertRows;
    internal override HttpDocumentStore ForClient(HttpClient client) => new ClickHouseDocumentStore(client, Name, Options);
    internal override async Task AdmitAsync(CancellationToken token)
    {
        using var result = await QueryAsync(Version, null, token).ConfigureAwait(false);
    }

    internal override async Task<string> InitializeAsync(CancellationToken token)
    {
        using var version = await QueryAsync(Version, null, token).ConfigureAwait(false);
        var observed = version.RootElement.GetProperty(Data)[FirstRow].GetProperty(VersionField).GetString()!;
        await ExecuteAsync(Create + Name, null, token).ConfigureAwait(false);
        OwnsNamespace = true;
        await ExecuteAsync(CreateTable + Table + Columns, null, token).ConfigureAwait(false);
        return observed;
    }

    internal override Task DropAsync(CancellationToken token) => ExecuteAsync(Drop + Name, null, token);

    internal override Task WriteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken token)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal) { [Id] = document.Id, [Body] = document.Json };
        var query = scenario switch
        {
            Scenario.DocumentWrite => Insert + Table + Values + Row(document),
            Scenario.DocumentUpdate => Update + Table + UpdateBody,
            Scenario.DocumentDelete => Update + Table + Delete,
            _ => throw new NotSupportedException()
        };
        return ExecuteAsync(query, scenario == Scenario.DocumentWrite ? null : parameters, token);
    }

    // The same JSONEachRow insert and request settings as the single create, with one row per document.
    internal override async Task CreateAsync(IReadOnlyList<BenchmarkDocument> documents, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, QueryPath(null));
        request.Content = Rows(Insert + Table + Values, documents, Row);
        await RequireEmptyAsync(request, token).ConfigureAwait(false);
    }

    private static string Row(BenchmarkDocument document)
        => JsonSerializer.Serialize(new { id = document.Id, body = document.Json, number = document.Number });

    internal override Task<JsonDocument> ReadAsync(string id, int? number, CancellationToken token)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal) { [Id] = id };
        if (number is { } numeric)
        {
            parameters[Number] = numeric.ToString(CultureInfo.InvariantCulture);
        }
        return QueryAsync(Select + Table + Predicate + (number.HasValue ? NumericPredicate : string.Empty) + Limit + JsonFormat, parameters, token);
    }

    internal override Task<JsonDocument> PageAsync(string after, CancellationToken token)
        => QueryAsync(Select + Table + Page + Policy.ReadbackBatchCapacity.ToString(CultureInfo.InvariantCulture) + JsonFormat,
            new Dictionary<string, string>(StringComparer.Ordinal) { [After] = after }, token);

    internal override async Task IndexAsync(CancellationToken token)
    {
        await ExecuteAsync(Update + Table + Index, null, token, indexing: true).ConfigureAwait(false);
        await ExecuteAsync(Update + Table + Materialize, null, token, indexing: true).ConfigureAwait(false);
    }

    private async Task ExecuteAsync(string sql, Dictionary<string, string>? parameters, CancellationToken token, bool indexing = false)
    {
        using var request = Request(sql, parameters);
        await RequireEmptyAsync(request, token, indexing).ConfigureAwait(false);
    }

    private async Task RequireEmptyAsync(HttpRequestMessage request, CancellationToken token, bool indexing = false)
    {
        if (!string.IsNullOrWhiteSpace(await SendAsync(request, token, indexing).ConfigureAwait(false)))
        {
            throw new ComparisonFailureException(NonemptyReply);
        }
    }

    private async Task<JsonDocument> QueryAsync(string sql, Dictionary<string, string>? parameters, CancellationToken token)
    {
        using var request = Request(sql, parameters);
        return JsonDocument.Parse(await SendAsync(request, token).ConfigureAwait(false));
    }

    private HttpRequestMessage Request(string sql, Dictionary<string, string>? parameters)
        => new(HttpMethod.Post, QueryPath(parameters)) { Content = new StringContent(sql, Encoding.UTF8, Media) };

    private string QueryPath(Dictionary<string, string>? parameters)
    {
        var path = Settings + Policy.ClickHouseInsertFlushMilliseconds.ToString(CultureInfo.InvariantCulture);
        if (parameters is not null)
        {
            foreach (var (key, value) in parameters)
            {
                path += ParameterPrefix + key + Equal + Uri.EscapeDataString(value);
            }
        }
        return path;
    }
}

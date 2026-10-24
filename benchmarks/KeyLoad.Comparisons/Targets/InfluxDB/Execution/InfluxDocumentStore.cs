using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal sealed class InfluxDocumentStore(HttpClient http, string name, IOptions<ComparisonExecutionOptions> options)
    : HttpDocumentStore(http, name, options)
{
    private const string TargetName = "InfluxDB";
    private const string Ack = "InfluxDB 3 Core; file object store; write_lp no_sync=false; one server WAL acknowledgement";
    private const string Auth = "isolated benchmark database; authentication disabled on session-only container";
    private const string Configure = "/api/v3/configure/database";
    private const string ConfigureTable = "/api/v3/configure/table", TableName = "documents";
    private const string Body = "body", StringType = "utf8", IntegerType = "int64";
    private const string DeleteDatabase = Configure + "?db=";
    private const string Query = "/api/v3/query_sql";
    private const string Write = "/api/v3/write_lp?precision=nanosecond&accept_partial=false&no_sync=false&db=";
    private const string Ping = "/ping";
    private const string VersionField = "version";
    private const string MissingVersion = "InfluxDatabaseVersionNotObserved";
    private const string IndexUnavailable = "InfluxDB 3 Core manages its indexes during ingestion; no equivalent post-load CREATE INDEX.";
    private const string Select = "SELECT id,body FROM documents WHERE id=$id";
    private const string NumericPredicate = " AND number>=$number AND number<$number+1";
    private const string Limit = " ORDER BY id LIMIT 2";
    private const string Page = "SELECT id,body FROM documents WHERE id>$after ORDER BY id LIMIT ";
    private const string Id = "id", Number = "number", After = "after", Json = "json";
    private const string LinePrefix = "documents,id=";
    private const string BodyField = " body=\"", NumberField = "\",number=", IntegerSuffix = "i ";
    private const string Backslash = "\\", EscapedBackslash = "\\\\", Quote = "\"", EscapedQuote = "\\\"";
    private const string Media = "text/plain";
    private const string NonemptyReply = "InfluxWriteReplyRejected";
    private const string Readiness = "SELECT 1 AS value";
    private const long EpochNanoseconds = 1767225600000000000;
    // InfluxDB 3 recommends 10,000 lines or 10 MB per write, whichever comes first; a canonical 1 KiB document line takes at
    // most 1,106 bytes, so 10 MB comes first: https://docs.influxdata.com/influxdb3/core/write-data/best-practices/optimize-writes/
    private const int WriteRequestBytes = 10_000_000, CanonicalLineBytes = 1_106;
    private const int WriteLines = WriteRequestBytes / CanonicalLineBytes;
    private const string OversizedWrite = "InfluxWriteBatchExceedsRequestBytes";
    internal override string Target => TargetName;
    internal override string Acknowledgement => Ack;
    internal override string Authorization => Auth;
    internal override bool SupportsDelete => false;
    internal override int BatchRecords => WriteLines;
    internal override HttpDocumentStore ForClient(HttpClient client) => new InfluxDocumentStore(client, Name, Options);
    internal override async Task AdmitAsync(CancellationToken token)
    {
        using var result = await QueryAsync(Readiness, new(StringComparer.Ordinal), token).ConfigureAwait(false);
    }

    internal override async Task<string> InitializeAsync(CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Ping);
        using var response = JsonDocument.Parse(await SendAsync(request, token).ConfigureAwait(false));
        var version = response.RootElement.GetProperty(VersionField).GetString();
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ComparisonFailureException(MissingVersion);
        }
        using var configure = new HttpRequestMessage(HttpMethod.Post, Configure);
        configure.Content = JsonContent.Create(new { db = Name });
        _ = await SendAsync(configure, token).ConfigureAwait(false);
        OwnsNamespace = true;
        using var table = new HttpRequestMessage(HttpMethod.Post, ConfigureTable);
        table.Content = JsonContent.Create(new
        {
            db = Name,
            table = TableName,
            tags = new[] { Id },
            fields = new[] { new { name = Body, type = StringType }, new { name = Number, type = IntegerType } }
        });
        _ = await SendAsync(table, token).ConfigureAwait(false);
        return version;
    }

    internal override async Task DropAsync(CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, DeleteDatabase + Name);
        _ = await SendAsync(request, token).ConfigureAwait(false);
    }

    internal override async Task WriteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken token)
    {
        if (scenario is not (Scenario.DocumentWrite or Scenario.DocumentUpdate))
        {
            throw new NotSupportedException();
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, Write + Name);
        request.Content = new StringContent(Line(document), Encoding.UTF8, Media);
        await RequireEmptyAsync(request, token).ConfigureAwait(false);
    }

    // One line-protocol write with the single create's precision, all-or-nothing and WAL-sync settings.
    internal override async Task CreateAsync(IReadOnlyList<BenchmarkDocument> documents, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Write + Name);
        request.Content = Rows(string.Empty, documents, Line);
        if (request.Content.Headers.ContentLength > WriteRequestBytes)
        {
            throw new ComparisonFailureException(OversizedWrite);
        }
        await RequireEmptyAsync(request, token).ConfigureAwait(false);
    }

    private static string Line(BenchmarkDocument document)
    {
        var escaped = document.Json.Replace(Backslash, EscapedBackslash, StringComparison.Ordinal)
            .Replace(Quote, EscapedQuote, StringComparison.Ordinal);
        return LinePrefix + document.Id + BodyField + escaped + NumberField
            + document.Number.ToString(CultureInfo.InvariantCulture) + IntegerSuffix
            + checked(EpochNanoseconds + document.Number).ToString(CultureInfo.InvariantCulture);
    }

    private async Task RequireEmptyAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(await SendAsync(request, token).ConfigureAwait(false)))
        {
            throw new ComparisonFailureException(NonemptyReply);
        }
    }

    internal override Task<JsonDocument> ReadAsync(string id, int? number, CancellationToken token)
    {
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal) { [Id] = id };
        if (number is { } numeric)
        {
            parameters[Number] = numeric;
        }
        return QueryAsync(Select + (number.HasValue ? NumericPredicate : string.Empty) + Limit, parameters, token);
    }

    internal override Task<JsonDocument> PageAsync(string after, CancellationToken token)
        => QueryAsync(Page + Policy.ReadbackBatchCapacity.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, object>(StringComparer.Ordinal) { [After] = after }, token);

    internal override Task IndexAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        throw new BenchmarkSessionCapabilityException(IndexUnavailable);
    }

    private async Task<JsonDocument> QueryAsync(string sql, Dictionary<string, object> parameters, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Query)
        { Content = JsonContent.Create(new { db = Name, q = sql, format = Json, @params = parameters }) };
        return JsonDocument.Parse(await SendAsync(request, token).ConfigureAwait(false));
    }
}

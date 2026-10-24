using System.Buffers;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal abstract class HttpDocumentStore(HttpClient http, string name, IOptions<ComparisonExecutionOptions> options)
{
    protected HttpClient Http { get; } = http;
    protected string Name { get; } = name;
    protected IOptions<ComparisonExecutionOptions> Options { get; } = ComparisonExecutionOptions.Require(options);
    protected ComparisonExecutionOptions Policy => Options.Value;
    internal bool OwnsNamespace { get; set; }
    internal abstract string Target { get; }
    internal abstract string Acknowledgement { get; }
    internal abstract string Authorization { get; }
    internal abstract bool SupportsDelete { get; }
    internal abstract HttpDocumentStore ForClient(HttpClient client);
    internal abstract Task AdmitAsync(CancellationToken token);
    internal abstract Task<string> InitializeAsync(CancellationToken token);
    internal abstract Task DropAsync(CancellationToken token);
    internal abstract Task WriteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken token);
    internal abstract int BatchRecords { get; }
    internal abstract Task CreateAsync(IReadOnlyList<BenchmarkDocument> documents, CancellationToken token);
    internal abstract Task<JsonDocument> ReadAsync(string id, int? number, CancellationToken token);
    internal abstract Task<JsonDocument> PageAsync(string after, CancellationToken token);
    internal abstract Task IndexAsync(CancellationToken token);

    internal static FoundDocument[] Documents(JsonDocument document)
    {
        const string Data = "data", Id = "id", Body = "body";
        var rows = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement : document.RootElement.GetProperty(Data);
        return rows.EnumerateArray().Select(row => new FoundDocument(
            row.GetProperty(Id).GetString()!, row.GetProperty(Body).GetString()!)).ToArray();
    }

    // Encodes each newline-terminated row straight to UTF-8, so a batch never holds a UTF-16 copy of its whole body.
    protected static ReadOnlyMemoryContent Rows(string head, IReadOnlyList<BenchmarkDocument> documents, Func<BenchmarkDocument, string> row)
    {
        const string Media = "text/plain", RowEnd = "\n";
        var body = new ArrayBufferWriter<byte>();
        Encoding.UTF8.GetBytes(head, body);
        foreach (var document in documents)
        {
            Encoding.UTF8.GetBytes(row(document), body);
            Encoding.UTF8.GetBytes(RowEnd, body);
        }
        var content = new ReadOnlyMemoryContent(body.WrittenMemory);
        content.Headers.ContentType = new MediaTypeHeaderValue(Media) { CharSet = Encoding.UTF8.WebName };
        return content;
    }

    protected async Task<string> SendAsync(HttpRequestMessage request, CancellationToken token, bool indexing = false)
    {
        const string Failed = "HttpDocumentDatabaseRequestFailed";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(indexing ? Policy.IndexBuildTimeout : Policy.OperationTimeout);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        var body = await ComparisonResponse.ReadTextAsync(response.Content, Policy, deadline.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ComparisonFailureException(Failed);
        }
        return body;
    }
}

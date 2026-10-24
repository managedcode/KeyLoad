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

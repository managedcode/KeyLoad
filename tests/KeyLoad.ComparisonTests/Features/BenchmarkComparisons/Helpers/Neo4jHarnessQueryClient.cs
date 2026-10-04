using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class Neo4jHarnessQueryClient : IDisposable
{
    private const int RequestTimeoutSeconds = 30;
    private const string DatabaseQueryPath = "db/neo4j/query/v2";
    private readonly HttpClient client;

    public Neo4jHarnessQueryClient(Uri endpoint, string password)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        client = CreateClient(endpoint, password);
    }

    public static HttpClient CreateClient(Uri endpoint, string password)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var http = new HttpClient { BaseAddress = EnsureTrailingSlash(endpoint), Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds) };
        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes("neo4j:" + password));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
        return http;
    }

    public async Task<Neo4jHarnessResponse> SendAsync(string statement, object? parameters, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);
        using var request = new HttpRequestMessage(HttpMethod.Post, DatabaseQueryPath)
        {
            Content = JsonContent.Create(new { statement, parameters = parameters ?? new { }, maxExecutionTime = RequestTimeoutSeconds })
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return new((int)response.StatusCode, document);
    }

    public void Dispose() => client.Dispose();

    private static Uri EnsureTrailingSlash(Uri endpoint) => endpoint.AbsoluteUri.EndsWith('/')
        ? endpoint : new Uri(endpoint.AbsoluteUri + "/", UriKind.Absolute);
}

internal sealed class Neo4jHarnessResponse(int statusCode, JsonDocument document) : IDisposable
{
    public int StatusCode { get; } = statusCode;
    public JsonDocument Document { get; } = document;

    public void Dispose() => Document.Dispose();
}

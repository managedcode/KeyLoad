using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class OpenSearchHttp
{
    private const int ExpectedCopiesDefault = 0;

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string JsonMediaType = "application/json";
    private const string NdjsonMediaType = "application/x-ndjson";

    internal static Task<JsonDocument> SendJsonAsync(HttpClient client, HttpMethod method, string path, object? body,
        CancellationToken cancellationToken, bool allowNotFound = false)
        => SendAsync(client, method, path, body is null ? null : JsonSerializer.Serialize(body, JsonOptions), JsonMediaType,
            allowNotFound, cancellationToken);

    internal static Task<JsonDocument> SendNdjsonAsync(HttpClient client, string path, string body, CancellationToken cancellationToken)
        => SendAsync(client, HttpMethod.Post, path, body, NdjsonMediaType, allowNotFound: false,
            cancellationToken: cancellationToken);

    internal static Task<JsonDocument> SendMutationAsync(HttpClient client, HttpMethod method, string path, object? body,
        Scenario scenario, int expectedCopies, CancellationToken cancellationToken)
        => SendAsync(client, method, path, body is null ? null : JsonSerializer.Serialize(body, JsonOptions), JsonMediaType,
            allowNotFound: false, cancellationToken, scenario, expectedCopies);

    private static async Task<JsonDocument> SendAsync(HttpClient client, HttpMethod method, string path, string? body,
        string contentType, bool allowNotFound, CancellationToken cancellationToken, Scenario? mutation = null, int expectedCopies = OpenSearchHttp.ExpectedCopiesDefault)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return JsonDocument.Parse(OpenSearchNames.EmptyObjectJson);
        }

        if (mutation is null)
        {
            response.EnsureSuccessStatusCode();
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        try
        {
            if (mutation is { } scenario)
            {
                OpenSearchWriteAcknowledgement.VerifyMutation(response.StatusCode, json.RootElement, scenario, expectedCopies);
            }
            return json;
        }
        catch (Exception)
        {
            json.Dispose();
            throw;
        }
    }
}

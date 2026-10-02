using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class OpenSearchHttp
{
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

    private static async Task<JsonDocument> SendAsync(HttpClient client, HttpMethod method, string path, string? body,
        string contentType, bool allowNotFound, CancellationToken cancellationToken)
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

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}

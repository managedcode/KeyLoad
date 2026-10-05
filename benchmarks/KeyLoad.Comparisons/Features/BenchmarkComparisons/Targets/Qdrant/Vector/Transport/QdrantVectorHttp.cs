using System.Net.Http.Json;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class QdrantVectorHttp
{
    private const int MaximumReplyBytes = 1_048_576;
    internal static async Task<JsonDocument> SendAsync(HttpClient client, HttpMethod method, string path,
        object? body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumReplyBytes)
            throw new ComparisonFailureException("QdrantVectorReplyLimit");
        await using var source = await response.Content.ReadAsStreamAsync(token);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(buffer, token);
            if (read == 0) break;
            if (bytes.Length + read > MaximumReplyBytes)
                throw new ComparisonFailureException("QdrantVectorReplyLimit");
            bytes.Write(buffer, 0, read);
        }
        return JsonDocument.Parse(bytes.ToArray());
    }
}

using System.Buffers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal static class QdrantVectorHttp
{
    internal static async Task<JsonDocument> SendAsync(HttpClient client, HttpMethod method, string path,
        object? body, IOptions<NativeComparisonExecutionOptions> executionOptions, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var policy = executionOptions.Value;
        if (response.Content.Headers.ContentLength > policy.MaxResponseBytes)
        {
            throw new ComparisonFailureException(QdrantVectorProtocol.ReplyLimit);
        }
        await using var source = await response.Content.ReadAsStreamAsync(token);
        using var bytes = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(policy.ReadBufferBytes);
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(QdrantVectorProtocol.FirstElementIndex, policy.ReadBufferBytes), token);
                if (read == QdrantVectorProtocol.EmptyCount)
                {
                    break;
                }
                if (bytes.Length + read > policy.MaxResponseBytes)
                {
                    throw new ComparisonFailureException(QdrantVectorProtocol.ReplyLimit);
                }
                await bytes.WriteAsync(buffer.AsMemory(QdrantVectorProtocol.FirstElementIndex, read), token);
            }
            return JsonDocument.Parse(bytes.ToArray());
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}

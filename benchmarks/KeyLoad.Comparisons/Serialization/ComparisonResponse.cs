using System.Buffers;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons;
/// <summary>Bounds actual execution HTTP body consumption, including chunked responses.</summary>
public static class ComparisonResponse
{
    private const int EmptyResultCount = 0;
    private const string LimitFailure = "ComparisonResponseLimitExceeded";
    /// <summary>Parses a response only after enforcing the configured body ceiling.</summary>
    /// <param name = "content">The execution HTTP response content.</param>
    /// <param name = "policy">The validated operational policy.</param>
    /// <param name = "token">The operation cancellation token.</param>
    /// <returns>The owned parsed JSON response.</returns>
    public static async Task<JsonDocument> ReadJsonAsync(HttpContent content, ComparisonExecutionOptions policy, CancellationToken token)
    {
        var body = await ReadBodyAsync(content, policy, token).ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }

    /// <summary>Bounds execution plaintext metadata replies.</summary>
    /// <param name = "content">Execution response body.</param>
    /// <param name = "policy">Validated operational policy.</param>
    /// <param name = "token">Operation cancellation token.</param>
    /// <returns>The bounded UTF8 execution metadata.</returns>
    public static async Task<string> ReadTextAsync(HttpContent content, ComparisonExecutionOptions policy, CancellationToken token) => Encoding.UTF8.GetString((await ReadBodyAsync(content, policy, token).ConfigureAwait(false)).Span);
    private static async Task<ReadOnlyMemory<byte>> ReadBodyAsync(HttpContent content, ComparisonExecutionOptions policy, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(policy);
        if (content.Headers.ContentLength > policy.MaxResponseBytes)
        {
            throw new ComparisonFailureException(LimitFailure);
        }

        await using var input = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var body = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(policy.ReadBufferBytes);
        try
        {
            while (true)
            {
                var count = await input.ReadAsync(buffer.AsMemory(EmptyResultCount, policy.ReadBufferBytes), token).ConfigureAwait(false);
                if (count == EmptyResultCount)
                {
                    break;
                }

                if (body.Length + count > policy.MaxResponseBytes)
                {
                    throw new ComparisonFailureException(LimitFailure);
                }

                await body.WriteAsync(buffer.AsMemory(EmptyResultCount, count), token).ConfigureAwait(false);
            }

            return body.GetBuffer().AsMemory(EmptyResultCount, checked((int)body.Length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

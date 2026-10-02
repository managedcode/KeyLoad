using System.Buffers;
using System.Text.Json;
using ManagedCode.Communication;

namespace KeyLoad.Client.Features.ClientApi;

internal static class BoundedProblemReader
{
    /// <summary>Maximum UTF-8 response body accepted when decoding an HTTP problem.</summary>
    internal const int MaximumProblemBodyBytes = 64 * 1024;

    public static async Task<Problem?> ReadAsync(HttpContent content, JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumProblemBodyBytes)
        {
            return null;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(MaximumProblemBodyBytes + 1);
        try
        {
            await using var body = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var count = 0;
            while (count <= MaximumProblemBodyBytes)
            {
                var read = await body.ReadAsync(buffer.AsMemory(count, MaximumProblemBodyBytes + 1 - count), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                count += read;
            }

            if (count > MaximumProblemBodyBytes)
            {
                return null;
            }

            return JsonSerializer.Deserialize<Problem>(buffer.AsSpan(0, count), options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}

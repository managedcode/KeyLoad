using System.Buffers;
using System.Text.Json;
using ManagedCode.Communication;

namespace KeyLoad.Client.Features.ClientApi;

internal static class BoundedProblemReader
{
    private const int OverflowProbeBytes = 1;
    private const int EmptyBodyLength = 0;
    private const int BodyStart = 0;

    public static async Task<Problem?> ReadAsync(HttpContent content, JsonSerializerOptions options,
        CancellationToken cancellationToken, int maximumProblemBodyBytes)
    {
        if (content.Headers.ContentLength > maximumProblemBodyBytes)
        {
            return null;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(maximumProblemBodyBytes + OverflowProbeBytes);
        try
        {
            await using var body = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var count = EmptyBodyLength;
            while (count <= maximumProblemBodyBytes)
            {
                var read = await body.ReadAsync(buffer.AsMemory(count, maximumProblemBodyBytes + OverflowProbeBytes - count), cancellationToken)
                    .ConfigureAwait(false);
                if (read == EmptyBodyLength)
                {
                    break;
                }

                count += read;
            }

            if (count > maximumProblemBodyBytes)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<Problem>(buffer.AsSpan(BodyStart, count), options);
            }
            catch (JsonException)
            {
                return null;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}

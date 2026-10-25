using System.Buffers;
using System.Text.Json;
using ManagedCode.Communication;

namespace KeyLoad.Client.ClientApi;

internal static class BoundedProblemReader
{
    public static async Task<(bool HasValue, Problem? Value)> ReadAsync(HttpContent content, JsonSerializerOptions options,
        int maximumProblemBodyBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maximumProblemBodyBytes)
        {
            return (false, default(Problem?));
        }

        var buffer = ArrayPool<byte>.Shared.Rent(maximumProblemBodyBytes + 1);
        try
        {
            await using var body = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var count = 0;
            while (count <= maximumProblemBodyBytes)
            {
                var read = await body.ReadAsync(buffer.AsMemory(count, maximumProblemBodyBytes + 1 - count), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                count += read;
            }

            if (count > maximumProblemBodyBytes)
            {
                return (false, default(Problem?));
            }

            try
            {
                return ((Problem?)(JsonSerializer.Deserialize<Problem>(buffer.AsSpan(0, count), options))) is { } outcomeValue3 ? (true, outcomeValue3) : (false, default(Problem?));
            }
            catch (JsonException)
            {
                return (false, default(Problem?));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}

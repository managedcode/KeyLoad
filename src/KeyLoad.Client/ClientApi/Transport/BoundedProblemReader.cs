using System.Buffers;
using System.Text.Json;
using ManagedCode.Communication;

namespace KeyLoad.Client.ClientApi;

internal static class BoundedProblemReader
{
    public static async Task<global::ManagedCode.Communication.Result<Problem?>> ReadAsync(HttpContent content, JsonSerializerOptions options,
        int maximumProblemBodyBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maximumProblemBodyBytes)
        {
            return global::ManagedCode.Communication.Result<Problem?>.FailNotFound(nameof(ReadAsync));
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
                return global::ManagedCode.Communication.Result<Problem?>.FailNotFound(nameof(ReadAsync));
            }

            try
            {
                return ((Problem?)(JsonSerializer.Deserialize<Problem>(buffer.AsSpan(0, count), options))) is { } outcomeValue3 ? global::ManagedCode.Communication.Result<Problem?>.Succeed(outcomeValue3) : global::ManagedCode.Communication.Result<Problem?>.FailNotFound(nameof(ReadAsync));
            }
            catch (JsonException)
            {
                return global::ManagedCode.Communication.Result<Problem?>.FailNotFound(nameof(ReadAsync));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}

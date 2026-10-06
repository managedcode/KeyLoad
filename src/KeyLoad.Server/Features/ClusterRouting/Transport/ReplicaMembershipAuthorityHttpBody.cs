using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class ReplicaMembershipAuthorityHttpBody
{
    internal static async Task<byte[]?> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        const int LengthStep = 1;
        const int StartEmptyCount = 0;

        if (request.ContentLength is not { } length || length > ReplicaMembershipAuthorityProtocol.MaximumRequestBytes)
        { return null; }
        var buffer = new byte[checked((int)length + LengthStep)];
        var received = await ReadDeclaredBodyAsync(request.Body, buffer, cancellationToken).ConfigureAwait(false);
        if (received != length || await HasTrailingByteAsync(request.Body, cancellationToken).ConfigureAwait(false))
        { return null; }
        return buffer.AsSpan(StartEmptyCount, received).ToArray();
    }

    private static async Task<int> ReadDeclaredBodyAsync(Stream body, byte[] buffer, CancellationToken cancellationToken)
    {
        const int ReceivedInitialValue = 0;
        const int EmptyCount = 0;

        var received = ReceivedInitialValue;
        while (received < buffer.Length)
        {
            var count = await body.ReadAsync(buffer.AsMemory(received), cancellationToken).ConfigureAwait(false);
            if (count == EmptyCount)
            { break; }
            received = checked(received + count);
        }
        return received;
    }

    private static async Task<bool> HasTrailingByteAsync(Stream body, CancellationToken cancellationToken)
    {
        const int HasTrailingByteAsyncElementCount = 1;
        const int EndOfBodyCount = 0;

        var trailing = new byte[HasTrailingByteAsyncElementCount];
        return await body.ReadAsync(trailing, cancellationToken).ConfigureAwait(false) != EndOfBodyCount;
    }
}

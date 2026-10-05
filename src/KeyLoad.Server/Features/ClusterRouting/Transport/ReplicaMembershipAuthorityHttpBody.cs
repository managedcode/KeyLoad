using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class ReplicaMembershipAuthorityHttpBody
{
    internal static async Task<byte[]?> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength is not { } length || length > ReplicaMembershipAuthorityProtocol.MaximumRequestBytes)
        { return null; }
        var buffer = new byte[checked((int)length + 1)];
        var received = await ReadDeclaredBodyAsync(request.Body, buffer, cancellationToken).ConfigureAwait(false);
        if (received != length || await HasTrailingByteAsync(request.Body, cancellationToken).ConfigureAwait(false))
        { return null; }
        return buffer.AsSpan(0, received).ToArray();
    }

    private static async Task<int> ReadDeclaredBodyAsync(Stream body, byte[] buffer, CancellationToken cancellationToken)
    {
        var received = 0;
        while (received < buffer.Length)
        {
            var count = await body.ReadAsync(buffer.AsMemory(received), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            { break; }
            received = checked(received + count);
        }
        return received;
    }

    private static async Task<bool> HasTrailingByteAsync(Stream body, CancellationToken cancellationToken)
    {
        var trailing = new byte[1];
        return await body.ReadAsync(trailing, cancellationToken).ConfigureAwait(false) != 0;
    }
}

using System.Net.Sockets;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3TransportClosed
{
    internal static async Task RequireAsync(Stream stream, CancellationToken originalDeadline)
    {
        bool closed;
        try
        {
            var bytes = new byte[PostgresRf3Protocol.OneColumn];
            closed = await stream.ReadAsync(bytes, originalDeadline).ConfigureAwait(false) == default(int);
        }
        catch (IOException failure) when (failure.InnerException is SocketException socket
            && socket.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted)
        {
            closed = true;
        }
        await global::TUnit.Assertions.Assert.That(originalDeadline.IsCancellationRequested).IsFalse();
        await global::TUnit.Assertions.Assert.That(closed).IsTrue();
    }
}

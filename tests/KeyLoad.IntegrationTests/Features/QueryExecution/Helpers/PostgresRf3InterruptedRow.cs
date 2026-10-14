using System.Buffers.Binary;
using System.Net.Sockets;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3InterruptedRow
{
    internal static async Task ReadAsync(PostgresRf3WireOwner wire, TaskCompletionSource<int> headerSeen,
        Task resumeReading, CancellationToken token)
    {
        try
        {
            var description = await PostgresRf3WireMessage.ReadAsync(wire.Stream, token).ConfigureAwait(false);
            await global::TUnit.Assertions.Assert.That(description.Type).IsEqualTo(PostgresRf3BackpressureProtocol.Description);
            var header = new byte[sizeof(byte) + sizeof(int) + sizeof(short) + sizeof(int)];
            await wire.Stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
            await global::TUnit.Assertions.Assert.That(header[PostgresRf3Protocol.FirstColumn]).IsEqualTo(PostgresRf3BackpressureProtocol.DataRow);
            var bytes = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(sizeof(byte))) - sizeof(int) - sizeof(short) - sizeof(int);
            await global::TUnit.Assertions.Assert.That(BinaryPrimitives.ReadInt16BigEndian(header.AsSpan(sizeof(byte) + sizeof(int)))).IsEqualTo((short)PostgresRf3Protocol.OneColumn);
            await global::TUnit.Assertions.Assert.That(BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(sizeof(byte) + sizeof(int) + sizeof(short)))).IsEqualTo(bytes);
            await global::TUnit.Assertions.Assert.That(bytes).IsGreaterThan(PostgresRf3BackpressureProtocol.MaximumEffectiveReceiveBytes);
            await global::TUnit.Assertions.Assert.That(bytes).IsLessThanOrEqualTo(PostgresRf3BackpressureProtocol.MaximumJsonBytes);
            headerSeen.SetResult(bytes);
            await resumeReading.WaitAsync(token).ConfigureAwait(false);
            var copied = await DrainAsync(wire.Stream, bytes, token).ConfigureAwait(false);
            await global::TUnit.Assertions.Assert.That(token.IsCancellationRequested).IsFalse();
            await global::TUnit.Assertions.Assert.That(copied).IsLessThan(bytes);
        }
        catch (Exception failure)
        {
            headerSeen.TrySetException(failure);
            throw;
        }
    }

    private static async Task<int> DrainAsync(Stream stream, int declaredBytes, CancellationToken token)
    {
        var copied = PostgresRf3Protocol.FirstColumn;
        var buffer = new byte[PostgresRf3BackpressureProtocol.MaximumEffectiveReceiveBytes];
        try
        {
            while (copied < declaredBytes)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(PostgresRf3Protocol.FirstColumn,
                    Math.Min(buffer.Length, declaredBytes - copied)), token).ConfigureAwait(false);
                if (count == default(int))
                {
                    break;
                }
                copied += count;
            }
        }
        catch (IOException failure) when (failure.InnerException is SocketException socket
            && socket.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted)
        {
        }
        return copied;
    }
}

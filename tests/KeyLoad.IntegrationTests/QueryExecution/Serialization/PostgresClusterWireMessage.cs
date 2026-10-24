using System.Buffers.Binary;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class PostgresClusterWireMessage
{
    internal static async Task<(byte Type, byte[] Payload)> ReadAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[sizeof(byte) + sizeof(int)];
        await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(sizeof(byte))) - sizeof(int);
        await global::TUnit.Assertions.Assert.That(length).IsGreaterThanOrEqualTo(default(int));
        await global::TUnit.Assertions.Assert.That(length).IsLessThanOrEqualTo(PostgresClusterBackpressureProtocol.MaximumMetadataBytes);
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, token).ConfigureAwait(false);
        return (header[PostgresClusterProtocol.FirstColumn], payload);
    }

    internal static async Task WriteAsync(Stream stream, byte type, byte[] payload, CancellationToken token)
    {
        var header = new byte[sizeof(byte) + sizeof(int)];
        header[PostgresClusterProtocol.FirstColumn] = type;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(sizeof(byte)), payload.Length + sizeof(int));
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        await stream.WriteAsync(payload, token).ConfigureAwait(false);
    }
}

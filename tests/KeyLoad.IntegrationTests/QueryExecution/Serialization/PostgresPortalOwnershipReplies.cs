using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresPortalOwnershipReplies
{
    internal static async Task ExactAsync(Stream stream, byte type, byte[] payload, CancellationToken token)
    {
        var actual = await PostgresClusterWireMessage.ReadAsync(stream, token);
        await global::TUnit.Assertions.Assert.That(actual.Type).IsEqualTo(type);
        await global::TUnit.Assertions.Assert.That(actual.Payload.AsSpan().SequenceEqual(payload)).IsTrue();
    }

    internal static async Task SendAsync(Stream stream, byte type, byte[] payload, CancellationToken token)
    {
        await PostgresClusterWireMessage.WriteAsync(stream, type, payload, token);
        await PostgresClusterWireMessage.WriteAsync(stream, PostgresPortalOwnershipProtocol.Flush, [], token);
    }

    internal static async Task ControlAsync(Stream stream, byte type, byte[] payload,
        byte completed, CancellationToken token)
    {
        await SendAsync(stream, type, payload, token);
        await ExactAsync(stream, completed, [], token);
    }

    internal static async Task RefusedAsync(Stream stream, byte type, byte[] payload,
        string error, CancellationToken token)
    {
        await SendAsync(stream, type, payload, token);
        await ExactAsync(stream, PostgresPortalOwnershipProtocol.Error, Encoding.UTF8.GetBytes(error), token);
        await PostgresClusterWireMessage.WriteAsync(stream, PostgresPortalOwnershipProtocol.Sync, [], token);
        await ExactAsync(stream, PostgresPortalOwnershipProtocol.Ready, [PostgresPortalOwnershipProtocol.Idle], token);
    }

    internal static async Task<QueryPage> PageAsync(Stream stream, CancellationToken token)
    {
        var actual = await PostgresClusterWireMessage.ReadAsync(stream, token);
        await global::TUnit.Assertions.Assert.That(actual.Type).IsEqualTo(PostgresPortalOwnershipProtocol.DataRow);
        var payload = actual.Payload;
        await global::TUnit.Assertions.Assert.That(payload.Length).IsGreaterThan(sizeof(short) + sizeof(int));
        await global::TUnit.Assertions.Assert.That(BinaryPrimitives.ReadInt16BigEndian(payload)).IsEqualTo((short)PostgresClusterProtocol.OneColumn);
        var length = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(sizeof(short)));
        await global::TUnit.Assertions.Assert.That(length).IsEqualTo(payload.Length - sizeof(short) - sizeof(int));
        var page = JsonSerializer.Deserialize<QueryPage>(payload.AsSpan(sizeof(short) + sizeof(int)), JsonDefaults.Options)
            ?? throw new InvalidOperationException(PostgresClusterProtocol.InvalidResult);
        await ExactAsync(stream, PostgresPortalOwnershipProtocol.CommandComplete,
            Encoding.UTF8.GetBytes(PostgresPortalOwnershipProtocol.Command), token);
        return page;
    }
}

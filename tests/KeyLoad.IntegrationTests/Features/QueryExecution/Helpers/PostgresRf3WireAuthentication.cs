using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3WireAuthentication
{
    internal static async Task<(int Id, int Secret)> AuthenticateAsync(PostgresRf3WireOwner wire,
        McpPersistedIdentity identity, CancellationToken token)
    {
        await wire.Stream.WriteAsync(PostgresRf3WireFrames.Startup(identity.Principal.Id), token).ConfigureAwait(false);
        await PostgresRf3WireFrames.PasswordChallengeAsync(wire.Stream, token).ConfigureAwait(false);
        var password = Encoding.UTF8.GetBytes(identity.Secret + Null);
        try
        {
            await PostgresRf3WireMessage.WriteAsync(wire.Stream, PostgresRf3BackpressureProtocol.Password, password, token);
        }
        finally { CryptographicOperations.ZeroMemory(password); }
        (int Id, int Secret)? backend = null;
        var authenticated = false;
        for (var index = PostgresRf3Protocol.FirstColumn; index < PostgresRf3BackpressureProtocol.MaximumBackendFrames; index++)
        {
            var frame = await PostgresRf3WireMessage.ReadAsync(wire.Stream, token).ConfigureAwait(false);
            if (frame.Type == Authentication)
            {
                await global::TUnit.Assertions.Assert.That(frame.Payload.Length).IsEqualTo(sizeof(int));
                await global::TUnit.Assertions.Assert.That(BinaryPrimitives.ReadInt32BigEndian(frame.Payload)).IsEqualTo(default(int));
                authenticated = true;
                continue;
            }
            if (frame.Type == PostgresRf3BackpressureProtocol.Backend)
            {
                await global::TUnit.Assertions.Assert.That(frame.Payload.Length).IsEqualTo(sizeof(int) + sizeof(int));
                backend = (BinaryPrimitives.ReadInt32BigEndian(frame.Payload),
                    BinaryPrimitives.ReadInt32BigEndian(frame.Payload.AsSpan(sizeof(int))));
                continue;
            }
            if (frame.Type == PostgresRf3BackpressureProtocol.Ready)
            {
                await global::TUnit.Assertions.Assert.That(authenticated).IsTrue();
                await global::TUnit.Assertions.Assert.That(backend.HasValue).IsTrue();
                await global::TUnit.Assertions.Assert.That(frame.Payload.AsSpan().SequenceEqual(Idle)).IsTrue();
                return backend!.Value;
            }
            await global::TUnit.Assertions.Assert.That(frame.Type).IsEqualTo(Parameter);
        }
        throw new InvalidOperationException(PostgresRf3Protocol.InvalidResult);
    }

    internal static async Task CancelAsync(ClusterFixture fixture, (int Id, int Secret) backend, CancellationToken token)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var control = await PostgresRf3WireOwner.OpenAsync(fixture, token);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var bytes = new byte[CancelFields * sizeof(int)];
                BinaryPrimitives.WriteInt32BigEndian(bytes, bytes.Length);
                BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(sizeof(int)), PostgresRf3BackpressureProtocol.CancelCode);
                BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(sizeof(int) + sizeof(int)), backend.Id);
                BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(sizeof(int) + sizeof(int) + sizeof(int)), backend.Secret);
                await control.Stream.WriteAsync(bytes, token).ConfigureAwait(false);
                await PostgresRf3TransportClosed.RequireAsync(control.Stream, token).ConfigureAwait(false);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private const byte Authentication = (byte)'R';
    private const byte Parameter = (byte)'S';
    private const int CancelFields = 4;
    private const string Null = "\0";
    private static readonly byte[] Idle = [(byte)'I'];
}

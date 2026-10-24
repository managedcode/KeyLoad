using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KeyLoad.Server;

internal sealed class PostgresCancellationRegistry
{
    private readonly Lock gate = new();
    private const string IdentityInUse = "The Postgres cancellation identity is already registered.";
    private readonly Dictionary<int, Registration> registrations = [];
    private int nextId;

    internal Registration Register()
    {
        lock (gate)
        {
            var id = unchecked(++nextId);
            if (id == PostgresProtocol.AllRows || registrations.ContainsKey(id))
            {
                throw Errors.Fail(ErrorCode.Conflict, IdentityInUse);
            }
            var secret = RandomNumberGenerator.GetBytes(sizeof(int));
            var registration = new Registration(id, secret, new PostgresCancellationSession());
            registrations.Add(id, registration);
            return registration;
        }
    }

    internal Task CancelAsync(int id, int secret)
    {
        lock (gate)
        {
            Span<byte> supplied = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(supplied, secret);
            return registrations.TryGetValue(id, out var registration)
                && CryptographicOperations.FixedTimeEquals(registration.Secret, supplied)
                ? registration.Session.CancelAsync() : Task.CompletedTask;
        }
    }

    internal async Task RemoveAsync(Registration registration)
    {
        lock (gate)
        {
            registrations.Remove(registration.Id);
        }
        try
        {
            await registration.Session.CloseAsync();
        }
        finally { CryptographicOperations.ZeroMemory(registration.Secret); }
    }

    internal sealed record Registration(int Id, byte[] Secret, PostgresCancellationSession Session);
}

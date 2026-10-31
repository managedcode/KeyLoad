using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Server;

internal sealed class PostgresSessionIdentity(string subject, byte[] credential,
    PostgresRetainedAdmission admission) : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private int disposed;
    internal string Subject { get; } = subject;

    internal async Task<PrincipalRecord> VerifyAsync(IServiceProvider services, ServerConnectionFeature owner,
        CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != PostgresProtocol.AllRows, this);
        var node = services.GetRequiredService<OrleansNode>();
        var requestId = Guid.NewGuid();
        PrincipalRecord? principal = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var connection = owner.Acquire(token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                principal = await CanonicalCredentialDispatch.AuthenticateAsync(node, Utf8.GetString(credential),
                    connection, requestId);
                if (!StringComparer.Ordinal.Equals(principal.Id, Subject))
                {
                    throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return principal ?? throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);
    }

    internal static PostgresSessionIdentity Create(string subject, ReadOnlySpan<byte> password,
        PostgresRetainedAdmission admission, int maximumBytes)
    {
        var reader = new PostgresPayloadReader(password);
        var value = reader.Text(maximumBytes);
        reader.Complete();
        if (value.Length == PostgresProtocol.AllRows)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);
        }
        var size = Utf8.GetByteCount(value);
        admission.Resize(PostgresProtocol.AllRows, size);
        try
        {
            return new(subject, Utf8.GetBytes(value), admission);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(() => admission.Resize(size, PostgresProtocol.AllRows), failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, sizeof(byte)) != PostgresProtocol.AllRows)
        {
            return;
        }
        CryptographicOperations.ZeroMemory(credential);
        admission.Resize(credential.Length, PostgresProtocol.AllRows);
    }
}

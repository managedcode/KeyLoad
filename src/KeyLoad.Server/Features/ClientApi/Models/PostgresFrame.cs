using System.Security.Cryptography;

namespace KeyLoad.Server;

internal sealed class PostgresFrame(byte type, byte[] payload, PostgresRetainedAdmission admission) : IDisposable
{
    private int disposed;
    internal byte Type { get; } = type;
    internal byte[] Payload { get; } = payload;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, sizeof(byte)) != PostgresProtocol.AllRows)
        {
            return;
        }
        CryptographicOperations.ZeroMemory(Payload);
        admission.Resize(Payload.Length, PostgresProtocol.AllRows);
    }
}

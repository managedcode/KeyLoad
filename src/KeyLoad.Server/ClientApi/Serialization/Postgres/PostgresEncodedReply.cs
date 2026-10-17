using System.Security.Cryptography;

namespace KeyLoad.Server;

internal sealed class PostgresEncodedReply(PostgresRetainedAdmission admission, int reservedBytes) : IDisposable
{
    private int disposed;
    internal bool PossibleWrite
    {
        get; set;
    }
    internal byte[] Payload { get; set; } = [];

    internal static PostgresEncodedReply Admit(PostgresSessionOptions profile, McpExecutionOptions execution,
        PostgresRetainedAdmission admission, out int maximumJsonBytes)
    {
        const int DataRowOverhead = sizeof(int) + sizeof(short) + sizeof(int) + sizeof(byte);
        maximumJsonBytes = Math.Min(execution.MaximumDataReplyBytes, profile.MaximumFrameBytes - DataRowOverhead);
        var retained = checked(maximumJsonBytes + maximumJsonBytes + DataRowOverhead);
        admission.Resize(PostgresProtocol.AllRows, retained);
        return new(admission, retained);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, sizeof(byte)) != PostgresProtocol.AllRows)
        {
            return;
        }
        CryptographicOperations.ZeroMemory(Payload);
        Payload = [];
        admission.Resize(reservedBytes, PostgresProtocol.AllRows);
    }
}

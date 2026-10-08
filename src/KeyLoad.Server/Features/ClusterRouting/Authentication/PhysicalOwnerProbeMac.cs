using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PhysicalOwnerProbeMac : IDisposable
{
    private static readonly byte[] RequestDomain = Encoding.ASCII.GetBytes(PhysicalOwnerProbeProtocol.RequestDomain);
    private static readonly byte[] ReplyDomain = Encoding.ASCII.GetBytes(PhysicalOwnerProbeProtocol.ReplyDomain);
    private const int ActiveState = 0;
    private const int DisposedState = 1;
    private readonly byte[] key;
    private int disposed;

    internal PhysicalOwnerProbeMac(ReadOnlySpan<byte> key)
    {
        if (key.Length != PhysicalOwnerProbeProtocol.SecretBytes)
        { throw Errors.Fail(ErrorCode.Validation, PhysicalOwnerProbeProtocol.InvalidProof); }
        this.key = key.ToArray();
    }

    internal static PhysicalOwnerProbeMac FromConfiguredSecret(string? secret)
    {
        if (secret is null)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof); }
        var key = Convert.FromBase64String(secret);
        try
        { return new(key); }
        finally
        { CryptographicOperations.ZeroMemory(key); }
    }

    internal string Sign(ReadOnlySpan<byte> body, bool reply)
        => Convert.ToHexStringLower(Digest(body, reply));

    internal bool Verify(ReadOnlySpan<byte> body, string signature, bool reply)
    {
        if (signature.Length != PhysicalOwnerProbeProtocol.SignatureCharacters
            || signature.Any(character => !char.IsAsciiHexDigitLower(character)))
        { return false; }
        var supplied = Convert.FromHexString(signature);
        var expected = Digest(body, reply);
        return CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private byte[] Digest(ReadOnlySpan<byte> body, bool reply)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != ActiveState, this);
        if (body.IsEmpty || body.Length > PhysicalOwnerProbeProtocol.MaximumBodyBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, PhysicalOwnerProbeProtocol.InvalidProof); }
        using var hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        hash.AppendData(reply ? ReplyDomain : RequestDomain);
        hash.AppendData(body);
        return hash.GetHashAndReset();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, DisposedState) == ActiveState)
        { CryptographicOperations.ZeroMemory(key); }
    }
}

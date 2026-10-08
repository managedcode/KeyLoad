using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteDocumentMac : IDisposable
{
    private static readonly byte[] RequestDomain = Encoding.ASCII.GetBytes(RemoteDocumentProtocol.RequestDomain);
    private static readonly byte[] ReplyDomain = Encoding.ASCII.GetBytes(RemoteDocumentProtocol.ReplyDomain);
    private const int ActiveState = 0;
    private const int DisposedState = 1;
    private readonly byte[] key;
    private int disposed;

    internal RemoteDocumentMac(ReadOnlySpan<byte> key)
    {
        if (key.Length != RemoteDocumentProtocol.SecretBytes)
        { throw Errors.Fail(ErrorCode.Validation, RemoteDocumentProtocol.InvalidProof); }
        this.key = key.ToArray();
    }

    internal static RemoteDocumentMac FromConfiguredSecret(string? secret)
    {
        if (secret is null)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
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
        if (signature.Length != RemoteDocumentProtocol.SignatureCharacters
            || signature.Any(character => !char.IsAsciiHexDigitLower(character)))
        { return false; }
        var supplied = Convert.FromHexString(signature);
        var expected = Digest(body, reply);
        return CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private byte[] Digest(ReadOnlySpan<byte> body, bool reply)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != ActiveState, this);
        if (body.IsEmpty || body.Length > RemoteDocumentProtocol.MaximumBodyBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteDocumentProtocol.InvalidProof); }
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

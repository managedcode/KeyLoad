using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns one configured peer key under distinct native movement request/reply purposes.</summary>
internal sealed class PartitionMovementMac : IDisposable
{
    private const string RequestPurpose = "keyload-partition-movement-request-v1";
    private const string ReplyPurpose = "keyload-partition-movement-reply-v1";
    private const int KeyBytes = 32;
    private const int SignatureCharacters = 64;
    private const int MinimumBodyBytes = 1;
    private const int Active = 0;
    private const int Disposed = 1;
    private const string InvalidProof = "The partition movement peer proof is invalid.";
    private static readonly byte[] RequestDomain = Encoding.ASCII.GetBytes(RequestPurpose);
    private static readonly byte[] ReplyDomain = Encoding.ASCII.GetBytes(ReplyPurpose);
    private readonly byte[] key;
    private readonly int maximumBytes;
    private int disposed;

    internal PartitionMovementMac(ReadOnlySpan<byte> configuredKey, int maximumBytes)
    {
        if (configuredKey.Length != KeyBytes || maximumBytes < MinimumBodyBytes)
        { throw Errors.Fail(ErrorCode.Validation, InvalidProof); }
        this.maximumBytes = maximumBytes;
        key = configuredKey.ToArray();
    }

    internal string Sign(ReadOnlySpan<byte> originalBody, bool reply)
        => Convert.ToHexStringLower(Digest(originalBody, reply));

    internal bool Verify(ReadOnlySpan<byte> originalBody, string signature, bool reply)
    {
        if (signature.Length != SignatureCharacters
            || signature.Any(character => !char.IsAsciiHexDigitLower(character)))
        { return false; }
        var supplied = Convert.FromHexString(signature);
        var expected = Digest(originalBody, reply);
        return CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private byte[] Digest(ReadOnlySpan<byte> originalBody, bool reply)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != Active, this);
        if (originalBody.IsEmpty || originalBody.Length > maximumBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidProof); }
        using var hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        hash.AppendData(reply ? ReplyDomain : RequestDomain);
        hash.AppendData(originalBody);
        return hash.GetHashAndReset();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, Disposed) == Active)
        { CryptographicOperations.ZeroMemory(key); }
    }
}

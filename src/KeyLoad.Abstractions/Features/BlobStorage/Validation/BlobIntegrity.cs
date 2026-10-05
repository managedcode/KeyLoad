using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad;

/// <summary>Implements version-one per-part SHA256 and scope-bound rolling integrity.</summary>
public static class BlobIntegrity
{
    /// <summary>The rolling algorithm name, distinct from whole-file SHA256.</summary>
    public const string IntegrityAlgorithm = "sha256-chain-v1";
    private const string ChainDomain = "blob-chain-v1";
    private const string GuidFormat = "N";
    private const string InvalidIntegrity = "The binary integrity value or layout is invalid.";
    private const int DigestBytes = 32;
    private const int DigestHexCharacters = DigestBytes * 2;
    private const int OrdinalBytes = sizeof(int);
    private const int LengthBytes = sizeof(int);
    private const int PartDigestOffset = DigestBytes + OrdinalBytes + LengthBytes;
    private const int ChainBytes = PartDigestOffset + DigestBytes;
    private const int FirstPartOrdinal = 0;
    private const int MinimumPartBytes = 1;
    private const int MinimumBlobBytes = 0;
    private const char FirstDecimalDigit = '0';
    private const char LastDecimalDigit = '9';
    private const char FirstHexLetter = 'a';
    private const char LastHexLetter = 'f';

    /// <summary>Creates the initial hash bound to creation incarnation, full scope and layout.</summary>
    /// <param name="incarnation">The immutable creation incarnation of this content.</param>
    /// <param name="blob">The full atomic object scope, including its partition key.</param>
    /// <param name="uploadId">The scoped upload identity.</param>
    /// <param name="length">The declared complete raw byte length.</param>
    /// <returns>The canonical lowercase initial chain hash.</returns>
    public static string InitialHash(Guid incarnation, BlobRef blob, Guid uploadId, long length)
    {
        ArgumentNullException.ThrowIfNull(blob);
        ArgumentNullException.ThrowIfNull(blob.Partition);
        if (incarnation == Guid.Empty || uploadId == Guid.Empty || length is < MinimumBlobBytes or > BlobLimits.MaximumBlobBytes)
        { throw Errors.Fail(ErrorCode.Validation, InvalidIntegrity); }
        var partition = blob.Partition;
        var preimage = KeyCodec.Encode(ChainDomain, incarnation.ToString(GuidFormat),
            partition.TenantId, partition.DatabaseId, partition.TransactionDomainId, partition.PartitionKey,
            blob.Resource, blob.Id, uploadId.ToString(GuidFormat), length, (long)BlobLimits.RawPartBytes);
        return PartHash(preimage);
    }

    /// <summary>Computes the SHA256 of exact raw part bytes.</summary>
    /// <param name="bytes">The raw bytes, without base64 or JSON encoding.</param>
    /// <returns>The canonical lowercase SHA256.</returns>
    public static string PartHash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>Advances the chain using its fixed 72-byte version-one preimage.</summary>
    /// <param name="previousHash">The canonical previous chain hash.</param>
    /// <param name="ordinal">The zero-based part ordinal.</param>
    /// <param name="length">The accepted raw length of this part.</param>
    /// <param name="partSha256">The canonical SHA256 of the raw part.</param>
    /// <returns>The next canonical rolling hash.</returns>
    public static string NextHash(string previousHash, int ordinal, int length, string partSha256)
    {
        if (ordinal < FirstPartOrdinal || ordinal >= BlobLimits.MaximumBlobBytes / BlobLimits.RawPartBytes
            || length is < MinimumPartBytes or > BlobLimits.RawPartBytes)
        { throw Errors.Fail(ErrorCode.Validation, InvalidIntegrity); }
        Span<byte> preimage = stackalloc byte[ChainBytes];
        Decode(previousHash).AsSpan().CopyTo(preimage);
        BinaryPrimitives.WriteInt32BigEndian(preimage.Slice(DigestBytes, OrdinalBytes), ordinal);
        BinaryPrimitives.WriteInt32BigEndian(preimage.Slice(DigestBytes + OrdinalBytes, LengthBytes), length);
        Decode(partSha256).AsSpan().CopyTo(preimage[PartDigestOffset..]);
        return PartHash(preimage);
    }

    /// <summary>Compares two strictly canonical hashes without data-dependent digest comparison.</summary>
    /// <param name="left">The first canonical lowercase hash.</param>
    /// <param name="right">The second canonical lowercase hash.</param>
    /// <returns>Whether their decoded digest bytes match.</returns>
    public static bool Matches(string left, string right)
        => CryptographicOperations.FixedTimeEquals(Decode(left), Decode(right));

    private static byte[] Decode(string hash)
    {
        if (hash is null || hash.Length != DigestHexCharacters)
        { throw Errors.Fail(ErrorCode.Validation, InvalidIntegrity); }
        foreach (var character in hash)
        {
            if (character is not (>= FirstDecimalDigit and <= LastDecimalDigit or >= FirstHexLetter and <= LastHexLetter))
            { throw Errors.Fail(ErrorCode.Validation, InvalidIntegrity); }
        }
        return Convert.FromHexString(hash);
    }
}

using System.Security.Cryptography;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static bool MatchesRemoteTransferProofHash(ReadOnlySpan<byte> proof, ReadOnlySpan<byte> retained)
    {
        if (proof.IsEmpty)
        { return retained.IsEmpty; }
        if (retained.Length != NativeAuthorityContract.DigestBytes)
        { return false; }
        Span<byte> digest = stackalloc byte[NativeAuthorityContract.DigestBytes];
        _ = SHA256.HashData(proof, digest);
        return CryptographicOperations.FixedTimeEquals(digest, retained);
    }
}

using System.Security.Cryptography;
using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static bool MatchesQueueRetryHash(ReadOnlySpan<byte> decisions, ReadOnlySpan<byte> signedHash)
    {
        if (decisions.IsEmpty)
        { return signedHash.IsEmpty; }
        if (signedHash.Length != QueueRetryProtocol.DigestBytes)
        { return false; }
        Span<byte> expected = stackalloc byte[QueueRetryProtocol.DigestBytes];
        _ = SHA256.HashData(decisions, expected);
        return CryptographicOperations.FixedTimeEquals(expected, signedHash);
    }
}

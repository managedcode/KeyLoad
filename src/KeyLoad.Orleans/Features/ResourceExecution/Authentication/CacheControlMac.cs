using System.Security.Cryptography;

namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlMac
{
    private const string ReplaceFailureMessage = "Unknown cache control shape.";

    internal static CacheControlDigest Compute(ReadOnlySpan<byte> key, ReadOnlySpan<byte> transcript)
    {
        Span<byte> mac = stackalloc byte[CacheControlDigest.ByteLength];
        _ = HMACSHA256.HashData(key, transcript, mac);
        return CacheControlDigest.FromBytes(mac);
    }

    internal static ICacheControlMessage Replace(ICacheControlMessage message, CacheControlDigest mac)
        => message switch
        {
            CacheReadyProof value => value with { Mac = mac },
            CachePrepareRequest value => value with { Mac = mac },
            CacheGrantRequest value => value with { Mac = mac },
            CacheRevokeRequest value => value with { Mac = mac },
            CacheRefreshHint value => value with { Mac = mac },
            CachePrepareReply value => value with { Mac = mac },
            CacheGrantReply value => value with { Mac = mac },
            CacheRevokeReply value => value with { Mac = mac },
            CacheRefreshReceipt value => value with { Mac = mac },
            _ => throw new ArgumentException(ReplaceFailureMessage, nameof(message))
        };
}

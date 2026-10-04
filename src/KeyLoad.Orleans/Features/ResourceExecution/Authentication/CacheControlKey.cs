using System.Security.Cryptography;

namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlKey
{
    private const int DerivationBufferBytes = 64;

    internal static byte[] Derive(ReadOnlySpan<byte> peerKey, CacheControlDigest trustedScope)
    {
        if (peerKey.Length != CacheControlDigest.ByteLength)
        {
            throw new ArgumentException("A peer key requires exactly 32 bytes.", nameof(peerKey));
        }

        if (trustedScope.FixedTimeEquals(default))
        {
            throw new ArgumentException("A trusted scope must be nondefault.", nameof(trustedScope));
        }

        Span<byte> input = stackalloc byte[DerivationBufferBytes];
        var writer = new CacheControlWriter(input);
        writer.String(CacheControlNames.KeyPurpose);
        writer.Digest(trustedScope);
        return HMACSHA256.HashData(peerKey, input[..writer.Position]);
    }
}

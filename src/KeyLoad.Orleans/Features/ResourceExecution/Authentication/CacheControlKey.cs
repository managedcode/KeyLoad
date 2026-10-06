using System.Security.Cryptography;

namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlKey
{
    private const int DerivationBufferBytes = 64;

    internal static byte[] Derive(ReadOnlySpan<byte> peerKey, CacheControlDigest trustedScope)
    {
        const string DeriveFailureMessage = "A peer key requires exactly 32 bytes.";
        const string DeriveDeriveFailureMessage = "A trusted scope must be nondefault.";

        if (peerKey.Length != CacheControlDigest.ByteLength)
        {
            throw new ArgumentException(DeriveFailureMessage, nameof(peerKey));
        }

        if (trustedScope.FixedTimeEquals(default))
        {
            throw new ArgumentException(DeriveDeriveFailureMessage, nameof(trustedScope));
        }

        Span<byte> input = stackalloc byte[DerivationBufferBytes];
        var writer = new CacheControlWriter(input);
        writer.String(CacheControlNames.KeyPurpose);
        writer.Digest(trustedScope);
        return HMACSHA256.HashData(peerKey, input[..writer.Position]);
    }
}

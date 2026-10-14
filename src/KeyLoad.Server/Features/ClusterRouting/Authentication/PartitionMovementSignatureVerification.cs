using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementSignatureVerification
{
    private const int SignatureCharacters = 64;

    internal static bool Verify(PartitionMovementMac mac, ReadOnlySpan<byte> body,
        string signature, ReadOnlySpan<byte> domain)
    {
        if (signature.Length != SignatureCharacters
            || signature.Any(character => !char.IsAsciiHexDigitLower(character)))
        {
            return false;
        }
        var supplied = Convert.FromHexString(signature);
        var expected = mac.DigestDomain(body, domain);
        return CryptographicOperations.FixedTimeEquals(supplied, expected);
    }
}

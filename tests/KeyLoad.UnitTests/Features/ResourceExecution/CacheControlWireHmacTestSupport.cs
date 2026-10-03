using System.Security.Cryptography;
using KeyLoad.Orleans.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.ResourceExecution.CacheControlWireAuthenticatorSupport;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class CacheControlWireHmacTestSupport
{
    internal static CacheControlDigest ComputeOuterMac(ICacheControlMessage message)
    {
        var encoded = CacheControlWire.TryEncodeForSigning(message, out var transcript);
        if (!encoded)
        {
            throw new InvalidOperationException(TranscriptFailureMessage);
        }

        var derivedKey = HMACSHA256.HashData(PeerKey(TestPeerKeySeed), Convert.FromHexString(KeyPurposeInputHex));
        var mac = HMACSHA256.HashData(derivedKey, transcript);
        return CacheControlDigest.FromBytes(mac);
    }

    internal const string KeyPurposeInputHex =
        "140000006b65796c6f61642d63616368652d6b65792d763111000000000000000000000000000000"
        + "00000000000000000000000000000012";
    private const byte TestPeerKeySeed = 0x21;
    private const string TranscriptFailureMessage = "The independently forged message has no valid signing transcript.";
}

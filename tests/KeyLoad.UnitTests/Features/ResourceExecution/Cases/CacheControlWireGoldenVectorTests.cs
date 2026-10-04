using System.Security.Cryptography;
using KeyLoad.Orleans.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.ResourceExecution.CacheControlWireAuthenticatorSupport;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireGoldenVectorTests
{
    [Test]
    public async Task AcCache014LiteralProofTranscriptAndKeyPurposeDriveTheRealHmac()
    {
        var proof = CacheControlWireTestData.Proof(CacheVoterSlot.Slot0, CacheVoterSlot.Slot0);
        var signing = CacheControlWire.TryEncodeForSigning(proof, out var actualSigning);
        var expectedSigning = Convert.FromHexString(ProofSigningHex);
        var expectedComplete = Convert.FromHexString(ProofCanonicalWithoutMacHex);
        var keyPurposeInput = Convert.FromHexString(KeyPurposeInputHex);
        var peerKey = PeerKey(0x21);
        var derivedKey = HMACSHA256.HashData(peerKey, keyPurposeInput);
        var expectedMac = HMACSHA256.HashData(derivedKey, expectedSigning);
        using var authenticator = CreateAuthenticator();
        var signed = authenticator.TrySign(proof, out var signedProof);

        await Assert.That(signing).IsTrue();
        await Assert.That(actualSigning.AsSpan().SequenceEqual(expectedSigning)).IsTrue();
        await Assert.That(keyPurposeInput.Length).IsEqualTo(KeyPurposeInputLength);
        await Assert.That(signed).IsTrue();
        await Assert.That(signedProof).IsNotNull();
        var actualMac = new byte[DigestLength];
        signedProof!.Mac.WriteBytes(actualMac);
        await Assert.That(actualMac.AsSpan().SequenceEqual(expectedMac)).IsTrue();

        var complete = CacheControlWire.TryEncodeSigned(signedProof, out var actualComplete);
        await Assert.That(complete).IsTrue();
        var expectedWithMac = JoinCanonicalMac(expectedComplete, expectedMac);
        await Assert.That(actualComplete.AsSpan().SequenceEqual(expectedWithMac)).IsTrue();
        await Assert.That(authenticator.TryAuthenticate(signedProof)).IsTrue();
    }

    [Test]
    public async Task AcCache014LiteralPrepareBytesBindTheRequestDigestAndSigningTranscript()
    {
        var request = CacheControlWireTestData.PrepareRequest();
        var complete = CacheControlWire.TryEncodeSigned(request, out var canonical);
        var signing = CacheControlWire.TryEncodeForSigning(request, out var transcript);
        var expectedCanonical = Convert.FromHexString(PrepareCanonicalHex);
        var expectedSigning = Convert.FromHexString(PrepareSigningHex);
        var expectedCorrelationInput = JoinCorrelationInput(expectedCanonical);
        var expectedDigest = SHA256.HashData(expectedCorrelationInput);
        var created = CacheControlCorrelation.TryCreate(request, out var correlation);

        await Assert.That(complete).IsTrue();
        await Assert.That(signing).IsTrue();
        await Assert.That(canonical.AsSpan().SequenceEqual(expectedCanonical)).IsTrue();
        await Assert.That(transcript.AsSpan().SequenceEqual(expectedSigning)).IsTrue();
        await Assert.That(expectedCorrelationInput.Length).IsEqualTo(CorrelationInputLength);
        await Assert.That(created).IsTrue();
        await Assert.That(correlation).IsNotNull();
        var actualDigest = new byte[DigestLength];
        correlation!.SignedRequestDigest.WriteBytes(actualDigest);
        await Assert.That(actualDigest.AsSpan().SequenceEqual(expectedDigest)).IsTrue();
    }

    [Test]
    public async Task AcCache014GoldenKeyPurposeInputMatchesTheForgeryOracleExactly()
    {
        var expected = Convert.FromHexString(KeyPurposeInputHex);
        var forgeryInput = Convert.FromHexString(CacheControlWireHmacTestSupport.KeyPurposeInputHex);

        await Assert.That(expected.Length).IsEqualTo(KeyPurposeInputLength);
        await Assert.That(forgeryInput.Length).IsEqualTo(KeyPurposeInputLength);
        await Assert.That(forgeryInput.AsSpan().SequenceEqual(expected)).IsTrue();
    }

    [Test]
    public async Task AcCache014FourFlatRejectionsMatchTheirCompleteLiteralCanonicalBytes()
    {
        ICacheControlMessage[] replies =
        [
            new CachePrepareReply(null, CacheControlStatus.Rejected, null, default),
            new CacheGrantReply(null, CacheControlStatus.Rejected, Guid.Empty, null, 0, default),
            new CacheRevokeReply(null, CacheControlStatus.Rejected, Guid.Empty, CacheRevokeEffect.None, default),
            new CacheRefreshReceipt(null, CacheControlStatus.Rejected, Guid.Empty, Guid.Empty, null, default)
        ];
        var expected = new[]
        {
            Convert.FromHexString(FlatPrepareReplyHex),
            Convert.FromHexString(FlatGrantReplyHex),
            Convert.FromHexString(FlatRevokeReplyHex),
            Convert.FromHexString(FlatRefreshReplyHex)
        };

        for (var index = 0; index < replies.Length; index++)
        {
            var encoded = CacheControlWire.TryEncodeSigned(replies[index], out var actual);
            await Assert.That(encoded).IsTrue();
            await Assert.That(actual.AsSpan().SequenceEqual(expected[index])).IsTrue();
        }
    }

    private static byte[] JoinCanonicalMac(byte[] canonicalWithoutMac, byte[] mac)
    {
        var complete = new byte[canonicalWithoutMac.Length + MacFieldLength + mac.Length];
        canonicalWithoutMac.CopyTo(complete, 0);
        complete[canonicalWithoutMac.Length] = MacFieldIdLow;
        complete[canonicalWithoutMac.Length + 1] = MacFieldIdHigh;
        mac.CopyTo(complete, canonicalWithoutMac.Length + MacFieldLength);
        return complete;
    }

    private static byte[] JoinCorrelationInput(byte[] canonicalRequest)
    {
        var prefix = Convert.FromHexString(CorrelationInputPrefixAndOpcodeHex);
        var input = new byte[prefix.Length + canonicalRequest.Length];
        prefix.CopyTo(input, 0);
        canonicalRequest.CopyTo(input, prefix.Length);
        return input;
    }

    private const string ProofSigningHex =
        "1c0000006b65796c6f61642d63616368652d72656164792d70726f6f662d763105240000006b6579" +
        "6c6f61642e63616368652e636f6e74726f6c2e72656164792d70726f6f662e763100000101001100" +
        "00000000000000000000000000000000000000000000000000000000001202003100000000000000" +
        "00000000000000000000000000000000000000000000003203000400000000000000040000050010" +
        "2030405060708090a0b0c0d0e003010600112233445566778899aabbccddeeff0207004233445566" +
        "778899aabbccddeeff030508000c00000000000000090080000000290000006b65796c6f61642e63" +
        "616368652e636f6e74726f6c2e706879736963616c2d62696e64696e672e76310000000100012345" +
        "6789abcdef80123456789abc0102005233445566778899aabbccddeeff0310030011000000313237" +
        "2e302e302e313a3131313131403704000123456789abcdef80123456789abc030500010a0001";
    private const string ProofCanonicalWithoutMacHex =
        "240000006b65796c6f61642e63616368652e636f6e74726f6c2e72656164792d70726f6f662e7631" +
        "00000101001100000000000000000000000000000000000000000000000000000000000012020031" +
        "00000000000000000000000000000000000000000000000000000000000032030004000000000000" +
        "000400000500102030405060708090a0b0c0d0e003010600112233445566778899aabbccddeeff02" +
        "07004233445566778899aabbccddeeff030508000c00000000000000090080000000290000006b65" +
        "796c6f61642e63616368652e636f6e74726f6c2e706879736963616c2d62696e64696e672e763100" +
        "000001000123456789abcdef80123456789abc0102005233445566778899aabbccddeeff03100300" +
        "110000003132372e302e302e313a3131313131403704000123456789abcdef80123456789abc0305" +
        "00010a0001";
    private const string KeyPurposeInputHex =
        "140000006b65796c6f61642d63616368652d6b65792d763111000000000000000000000000000000" +
        "00000000000000000000000000000012";
    private const string PrepareCanonicalHex =
        "280000006b65796c6f61642e63616368652e636f6e74726f6c2e707265706172652d726571756573" +
        "742e76310000be0000001f0000006b65796c6f61642e63616368652e636f6e74726f6c2e68656164" +
        "65722e76310000010100010200110000000000000000000000000000000000000000000000000000" +
        "00000000120300310000000000000000000000000000000000000000000000000000000000003204" +
        "000400000000000000050000060001010700102030405060708090a0b0c0d0e00301080011223344" +
        "5566778899aabbccddeeff0209002233445566778899aabbccddeeff03030a00007447dca2010000" +
        "0100110000003132372e302e302e323a313131313240370200710000000000000000000000000000" +
        "0000000000000000000000000000000072";
    private const string PrepareSigningHex =
        "200000006b65796c6f61642d63616368652d636f6e74726f6c2d726571756573742d763101280000" +
        "006b65796c6f61642e63616368652e636f6e74726f6c2e707265706172652d726571756573742e76" +
        "310000be0000001f0000006b65796c6f61642e63616368652e636f6e74726f6c2e6865616465722e" +
        "76310000010100010200110000000000000000000000000000000000000000000000000000000000" +
        "00120300310000000000000000000000000000000000000000000000000000000000003204000400" +
        "000000000000050000060001010700102030405060708090a0b0c0d0e00301080011223344556677" +
        "8899aabbccddeeff0209002233445566778899aabbccddeeff03030a00007447dca2010000010011" +
        "0000003132372e302e302e323a31313131324037";
    private const string CorrelationInputPrefixAndOpcodeHex =
        "240000006b65796c6f61642d63616368652d726571756573742d636f7272656c6174696f6e2d7631" +
        "01";
    private const string FlatPrepareReplyHex =
        "260000006b65796c6f61642e63616368652e636f6e74726f6c2e707265706172652d7265706c792e" +
        "76310000000100090200000300000000000000000000000000000000000000000000000000000000" +
        "0000000000";
    private const string FlatGrantReplyHex =
        "240000006b65796c6f61642e63616368652e636f6e74726f6c2e6772616e742d7265706c792e7631" +
        "00000001000902000000000000000000000000000000000003000004000000000000000000050000" +
        "00000000000000000000000000000000000000000000000000000000000000";
    private const string FlatRevokeReplyHex =
        "250000006b65796c6f61642e63616368652e636f6e74726f6c2e7265766f6b652d7265706c792e76" +
        "31000000010009020000000000000000000000000000000000030000040000000000000000000000" +
        "00000000000000000000000000000000000000000000";
    private const string FlatRefreshReplyHex =
        "260000006b65796c6f61642e63616368652e636f6e74726f6c2e726566726573682d7265706c792e" +
        "76310000000100090200000000000000000000000000000000000300000000000000000000000000" +
        "00000000040000050000000000000000000000000000000000000000000000000000000000000000" +
        "00";
    private const int CorrelationInputLength = 338;
    private const int KeyPurposeInputLength = 56;
    private const int DigestLength = 32;
    private const int MacFieldLength = 2;
    private const byte MacFieldIdLow = 0x0B;
    private const byte MacFieldIdHigh = 0x00;
}

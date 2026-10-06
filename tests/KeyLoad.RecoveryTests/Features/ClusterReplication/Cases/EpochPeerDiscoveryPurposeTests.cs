using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal sealed class EpochPeerDiscoveryPurposeTests
{
    private const string UnsupportedPurpose = "unsupported-peer-discovery-purpose";
    private const string CurrentPurpose = "keyload-discovery-request-data-epoch7";

    [Test]
    public async Task AcEpoch005UnsupportedSignedDiscoveryGetIsRejectedWithoutSpendingNonceAdmission()
    {
        using var fixture = new PeerDiscoveryFixture();
        using var unsupported = Sign(fixture.Secret, UnsupportedPurpose, Guid.NewGuid());
        await Assert.That(await fixture.Receiver.ValidateAsync(PeerDiscoveryFixture.Incoming(unsupported),
            PeerDiscoveryFixture.Cancellation)).IsFalse();

        using var current = fixture.Sign();
        await Assert.That(await fixture.Receiver.ValidateAsync(PeerDiscoveryFixture.Incoming(current),
            PeerDiscoveryFixture.Cancellation)).IsTrue();
        await Assert.That(PeerDiscoveryProtocol.Purpose).IsEqualTo(CurrentPurpose);
    }

    private static HttpRequestMessage Sign(ReadOnlySpan<byte> secret, string purpose, Guid nonce)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            PeerDiscoveryFixture.Origin + ReplicaProtocol.DiscoveryPath);
        var uri = request.RequestUri!;
        var timestamp = TimeProvider.System.GetUtcNow().ToUnixTimeMilliseconds()
            .ToString(CultureInfo.InvariantCulture);
        var nonceText = nonce.ToString(PeerDiscoveryProtocol.NonceFormat);
        var message = string.Join(PeerDiscoveryProtocol.Separator, purpose, request.Method.Method,
            uri.Authority, uri.AbsolutePath, timestamp, nonceText);
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(message)));
        request.Headers.Add(PeerDiscoveryProtocol.TimeHeader, timestamp);
        request.Headers.Add(PeerDiscoveryProtocol.NonceHeader, nonceText);
        request.Headers.Add(PeerDiscoveryProtocol.SignatureHeader, signature);
        return request;
    }
}

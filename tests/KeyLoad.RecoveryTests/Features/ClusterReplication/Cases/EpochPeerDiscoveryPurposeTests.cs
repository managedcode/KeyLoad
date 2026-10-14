using KeyLoad.Clustering;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

[PeerDiscoveryDataSource]
internal sealed class EpochPeerDiscoveryPurposeTests(PeerDiscoveryNativeCluster cluster)
{
    [Test]
    public async Task AcEpoch005UnsupportedNativeDiscoveryPurposeRefusesWithoutSpendingNonceAdmission()
    {
        await PeerDiscoveryFixture.RunAsync(cluster, async fixture =>
        {
            var original = fixture.Sign();
            foreach (var purpose in new[] { "unsupported-peer-discovery-purpose", ReplicaNativeDiscoveryProtocol.ReplyPurpose })
            {
                var signed = PeerDiscoveryReplayTests.Resign(fixture, original, purpose);
                await Assert.That((await fixture.Runtime.DiscoverAsync(signed, PeerDiscoveryFixture.Cancellation)).Error)
                    .IsEqualTo(ErrorCode.Unauthenticated);
            }
            await Assert.That((await fixture.ReceiveAsync(original)).Error).IsNull();
            await Assert.That((await fixture.ReceiveNativeAsync(fixture.Sign())).Error).IsNull();
        });
    }
}

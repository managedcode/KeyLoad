using System.Reflection;
using System.Security.Cryptography;
using KeyLoad.Clustering;

namespace KeyLoad.RecoveryTests;

/// <summary>AC-CRS-003: actual peer signers own their copied keys and independently owned handlers borrow them.</summary>
[PeerDiscoveryDataSource]
internal sealed class PeerDiscoveryOwnershipTests(PeerDiscoveryNativeCluster cluster)
{
    [Test]
    public async Task OwnedNativeCredentialIsDistinctAndClearedWithoutChangingCallerBytes()
    {
        await PeerDiscoveryFixture.RunAsync(cluster, async fixture =>
        {
            var original = fixture.Secret.ToArray();
            try
            {
                var owned = OwnedCredential(fixture.Sender);
                await Assert.That(ReferenceEquals(owned, fixture.Secret)).IsFalse();
                await Assert.That(owned.SequenceEqual(original)).IsTrue();
                fixture.Sender.Dispose();
                await Assert.That(owned.All(value => value == 0)).IsTrue();
                await Assert.That(fixture.Secret.SequenceEqual(original)).IsTrue();
                fixture.Sender.Dispose();
                await Assert.That(owned.All(value => value == 0)).IsTrue();
            }
            finally { CryptographicOperations.ZeroMemory(original); }
        });
    }

    [Test]
    public async Task ClosedNativeRuntimeRejectsBeforeBorrowedAuthenticationOrMembership()
    {
        await PeerDiscoveryFixture.RunAsync(cluster, async fixture =>
        {
            var signed = fixture.Sign();
            await fixture.Runtime.StopAsync();
            fixture.Receiver.Dispose();
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => fixture.Runtime.DiscoverAsync(signed, PeerDiscoveryFixture.Cancellation));
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Runtime.ReadActiveMembership());
            await fixture.Runtime.StopAsync();
        });
    }

    [Test]
    public async Task DisposingActualNativeRuntimeLeavesBorrowedSignerUsable()
    {
        await PeerDiscoveryFixture.RunAsync(cluster, async fixture =>
        {
            await fixture.Runtime.DisposeAsync();
            var signed = fixture.Sign();
            await Assert.That((await fixture.ReceiveNativeAsync(signed)).Error).IsNull();
            await Assert.That(OwnedCredential(fixture.Sender).SequenceEqual(fixture.Secret)).IsTrue();
            await Assert.That(OwnedCredential(fixture.Receiver).SequenceEqual(fixture.Secret)).IsTrue();
        });
    }

    [Test]
    public Task ActualNativeCallerCancellationReleasesAdmissionThenHealthyReplayAndJoinedStop()
        => PeerDiscoveryFixture.RunAsync(cluster, fixture => NativeDiscoveryCancellationFlow.RunAsync(cluster, fixture));

    private static byte[] OwnedCredential(ReplicaEnvelopeAuthenticator authentication)
    {
        var mac = typeof(ReplicaEnvelopeAuthenticator).GetField("mac", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(authentication)!;
        return (byte[])mac.GetType().GetField("secret", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mac)!;
    }
}

using System.Reflection;
using System.Security.Cryptography;
using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests;

/// <summary>AC-CRS-003: actual peer signers own their copied keys and independently owned handlers borrow them.</summary>
internal sealed class PeerDiscoveryOwnershipTests
{
    [Test]
    public async Task OwnedCredentialIsDistinctAndClearedWithoutChangingTheCaller()
    {
        using var fixture = new PeerDiscoveryFixture();
        var original = fixture.Secret.ToArray();
        try
        {
            var owned = OwnedCredential(fixture.Sender);
            await Assert.That(ReferenceEquals(owned, fixture.Secret)).IsFalse();
            await Assert.That(owned.SequenceEqual(original)).IsTrue();
            fixture.Sender.Dispose();
            await Assert.That(owned.All(static value => value == 0)).IsTrue();
            await Assert.That(fixture.Secret.SequenceEqual(original)).IsTrue();
            fixture.Sender.Dispose();
            await Assert.That(owned.All(static value => value == 0)).IsTrue();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(original);
        }
    }

    [Test]
    public async Task DisposedSignerRejectsSigningHandlerCreationAndReceiverValidation()
    {
        using var fixture = new PeerDiscoveryFixture();
        using var signed = fixture.Sign();
        fixture.Sender.Dispose();
        fixture.Receiver.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Sender.Sign(signed));
        Assert.ThrowsExactly<ObjectDisposedException>(() =>
        {
            using var handler = fixture.Sender.CreateHandler();
        });
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
            fixture.Receiver.ValidateAsync(PeerDiscoveryFixture.Incoming(signed), PeerDiscoveryFixture.Cancellation));
    }

    [Test]
    public async Task DisposingTheRealSocketHandlerLeavesItsBorrowedSignerUsable()
    {
        using var fixture = new PeerDiscoveryFixture();
        using (var handler = fixture.Sender.CreateHandler())
        {
            await Assert.That(((DelegatingHandler)handler).InnerHandler).IsTypeOf<SocketsHttpHandler>();
        }
        using var signed = fixture.Sign();
        await Assert.That(await fixture.Receiver.ValidateAsync(
            PeerDiscoveryFixture.Incoming(signed), PeerDiscoveryFixture.Cancellation)).IsTrue();
        await Assert.That(OwnedCredential(fixture.Sender).SequenceEqual(fixture.Secret)).IsTrue();
    }

    private static byte[] OwnedCredential(PeerSecurity security) =>
        (byte[])typeof(PeerSecurity).GetField("secret", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(security)!;
}

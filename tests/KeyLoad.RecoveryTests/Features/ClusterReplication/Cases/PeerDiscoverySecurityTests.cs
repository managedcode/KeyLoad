using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.RecoveryTests;

/// <summary>AC-REP-001/005/006: actual signed discovery requests reject every route, body and authentication mutation.</summary>
[PeerDiscoveryDataSource]
internal sealed class PeerDiscoverySecurityTests(PeerDiscoveryNativeCluster cluster)
{
    private const string WrongCaseReceiverVoter = "secondary_1";

    [Test]
    public async Task GenuineNativeSignUsesCopiedCredentialAndReplayIsRejected()
    {
        await PeerDiscoveryFixture.RunAsync(cluster, async fixture =>
        {
            Array.Clear(fixture.Secret);
            var first = fixture.Sign();
            var next = fixture.Sign();
            await Assert.That(first.Nonce == next.Nonce).IsFalse();
            var reply = await fixture.ReceiveNativeAsync(first);
            await Assert.That(reply.Error).IsNull();
            var state = NativeSerialization.Deserialize<ReplicaSiloDiscovery>(reply.Payload.Span);
            await Assert.That(state.VoterId).IsEqualTo(PeerDiscoveryNativeCluster.ReceiverVoter);
            await Assert.That(state.SiloAddress).IsEqualTo(cluster.Receiver.Discovery.RuntimeAddress);
            await Assert.That(state.TransportReady).IsTrue();
            await Assert.That((await fixture.ReceiveNativeAsync(first)).Error).IsEqualTo(ErrorCode.Unauthenticated);
        });
    }

    [Test]
    [Arguments(PeerDiscoveryNativeMutation.Version)]
    [Arguments(PeerDiscoveryNativeMutation.Caller)]
    [Arguments(PeerDiscoveryNativeMutation.CallerAddress)]
    [Arguments(PeerDiscoveryNativeMutation.Destination)]
    [Arguments(PeerDiscoveryNativeMutation.DestinationAddress)]
    [Arguments(PeerDiscoveryNativeMutation.Nonce)]
    [Arguments(PeerDiscoveryNativeMutation.Expiry)]
    [Arguments(PeerDiscoveryNativeMutation.Signature)]
    [Arguments(PeerDiscoveryNativeMutation.MissingCaller)]
    [Arguments(PeerDiscoveryNativeMutation.MissingCallerAddress)]
    [Arguments(PeerDiscoveryNativeMutation.MissingDestination)]
    [Arguments(PeerDiscoveryNativeMutation.MissingDestinationAddress)]
    [Arguments(PeerDiscoveryNativeMutation.MissingSignature)]
    [Arguments(PeerDiscoveryNativeMutation.DuplicateSignature)]
    [Arguments(PeerDiscoveryNativeMutation.OversizedCaller)]
    [Arguments(PeerDiscoveryNativeMutation.MalformedSignature)]
    public async Task RejectedNativeRequestDoesNotConsumeNonceAdmission(PeerDiscoveryNativeMutation mutation)
    {
        await PeerDiscoveryFixture.RunAsync(cluster, async fixture =>
        {
            var original = fixture.Sign();
            var changed = PeerDiscoveryNativeMutations.Apply(original, mutation);
            await Assert.That((await fixture.Runtime.DiscoverAsync(changed, PeerDiscoveryFixture.Cancellation)).Error)
                .IsEqualTo(ErrorCode.Unauthenticated);
            await Assert.That((await fixture.Runtime.DiscoverAsync(changed, PeerDiscoveryFixture.Cancellation)).Error)
                .IsEqualTo(ErrorCode.Unauthenticated);
            await Assert.That((await fixture.ReceiveAsync(original)).Error).IsNull();
        });
    }

    [Test]
    public async Task OutboundNativeContractErrorsAreTypedBeforeTransport()
    {
        await PeerDiscoveryFixture.RunAsync(cluster, async fixture =>
        {
            await Assert.That(string.Equals(WrongCaseReceiverVoter, PeerDiscoveryNativeCluster.ReceiverVoter,
                StringComparison.OrdinalIgnoreCase)).IsTrue();
            await Assert.That(string.Equals(WrongCaseReceiverVoter, PeerDiscoveryNativeCluster.ReceiverVoter,
                StringComparison.Ordinal)).IsFalse();
            foreach (var voter in new[] { string.Empty, "unconfigured", WrongCaseReceiverVoter })
            {
                var error = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Sender.CreateNativeDiscoveryRequest(
                    voter, cluster.Receiver.Discovery.RuntimeAddress));
                await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
            }
            foreach (var address in new[] { string.Empty, " ", "invalid-native-address",
                cluster.Receiver.Discovery.RuntimeAddress + " ",
                new string('x', ReplicaTransportProtocol.MaximumAddressCharacters + 1) })
            {
                var invalid = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Sender.CreateNativeDiscoveryRequest(
                    PeerDiscoveryNativeCluster.ReceiverVoter, address));
                await Assert.That(invalid.Code).IsEqualTo(ErrorCode.Validation);
                await Assert.That(invalid.Message).IsEqualTo(ReplicaProtocol.InvalidPeer);
            }
            await Assert.That((await fixture.ReceiveAsync(fixture.Sign())).Error).IsNull();
        });
    }

    [Test]
    public async Task OriginalNativeExpiryUsesTheConfiguredRpcDeadline()
    {
        await PeerDiscoveryFixture.RunAsync(cluster, async fixture =>
        {
            var before = TimeProvider.System.GetUtcNow();
            var original = fixture.Sign();
            var after = TimeProvider.System.GetUtcNow();
            var issued = original.ExpiresAt - cluster.Sender.Configuration.RpcTimeout;
            await Assert.That(issued >= before.AddMilliseconds(-1) && issued <= after).IsTrue();
            await Assert.That(original.ExpiresAt.Offset).IsEqualTo(TimeSpan.Zero);
            await Assert.That((await fixture.ReceiveNativeAsync(original)).Error).IsNull();
        });
    }

    [Test]
    public async Task InvalidNativeCredentialAndReplayConfigurationRefuseBeforeAdmission()
    {
        await PeerDiscoveryFixture.RunAsync(cluster, async fixture =>
        {
            foreach (var length in new[] { 0, ReplicaTransportProtocol.SecretBytes - 1, ReplicaTransportProtocol.SecretBytes + 1 })
            {
                Assert.ThrowsExactly<InvalidOperationException>(() =>
                {
                    using var invalid = PeerDiscoveryFixture.CreateAuthentication(cluster.Sender, new byte[length]);
                });
            }
            Assert.ThrowsExactly<OptionsValidationException>(() => new OrleansMembershipOptions { ReplayNonceCapacity = 0 }.Validate());
            Assert.ThrowsExactly<OptionsValidationException>(() => new OrleansMembershipOptions { MaximumAdmissions = 0 }.Validate());
            await Assert.That((await fixture.ReceiveAsync(fixture.Sign())).Error).IsNull();
        });
    }
}

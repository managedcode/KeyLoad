using System.Security.Cryptography;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.RecoveryTests;

/// <summary>AC-REP-006: authenticated discovery replay state has strict temporal and fixed-capacity boundaries.</summary>
[PeerDiscoveryDataSource]
internal sealed class PeerDiscoveryReplayTests(PeerDiscoveryNativeCluster cluster)
{
    [Test]
    public async Task RealClockRejectsStaleFutureAndExtremeSignedExpiryWithoutAdmission()
    {
        await PeerDiscoveryFixture.RunAsync(cluster, async fixture =>
        {
            var now = TimeProvider.System.GetUtcNow();
            foreach (var expires in new[] { now.AddMinutes(-2), now.AddMinutes(2), DateTimeOffset.MinValue, DateTimeOffset.MaxValue })
            {
                var request = Resign(fixture, fixture.Sign() with { ExpiresAt = expires });
                await Assert.That((await fixture.Runtime.DiscoverAsync(request, PeerDiscoveryFixture.Cancellation)).Error)
                    .IsEqualTo(ErrorCode.Unauthenticated);
            }
            await Assert.That((await fixture.ReceiveAsync(fixture.Sign())).Error).IsNull();
        });
    }

    [Test]
    public async Task OriginalNativeReplayAlgorithmRetainsExpiryInclusivelyAndPrunesForReuse()
    {
        await PeerDiscoveryFixture.RunAsync(cluster, async fixture =>
        {
            var original = fixture.Sign();
            var clock = new PeerDiscoveryReplayClock(TimeProvider.System.GetUtcNow());
            var options = new OrleansMembershipOptions { ReplayNonceCapacity = 1 };
            var replay = new ReplicaNativeDiscoveryReplay(Options.Create(options), clock);
            original = original with { ExpiresAt = clock.GetUtcNow() + options.ReplayLifetime + TimeSpan.FromSeconds(1) };
            replay.Admit(original);
            clock.Set(original.ExpiresAt);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => replay.Admit(original)).Code).IsEqualTo(ErrorCode.Unauthenticated);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => replay.Admit(original with { Nonce = Guid.NewGuid() })).Code)
                .IsEqualTo(ErrorCode.ResourceExhausted);
            clock.Set(original.ExpiresAt.AddMilliseconds(1));
            replay.Admit(original with { ExpiresAt = clock.GetUtcNow().AddSeconds(1) });
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => replay.Admit(original)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        });
    }

    [Test]
    public async Task AuthenticatedNativeCapacityDoesNotHideReplayOrForgedInput()
    {
        await PeerDiscoveryFixture.RunAsync(cluster, async fixture =>
        {
            var first = fixture.Sign();
            var second = fixture.Sign();
            var expected = cluster.Receiver.Discovery.Read();
            await NativeDiscoveryGenerationFlow.RejectWithoutConsumingAsync(fixture, first);
            var accepted = await fixture.ReceiveAsync(first);
            await Assert.That(accepted.Error).IsNull();
            await Assert.That(accepted.Nonce).IsEqualTo(first.Nonce);
            await Assert.That(NativeSerialization.Deserialize<ReplicaSiloDiscovery>(accepted.Payload.Span)).IsEqualTo(expected);
            var forged = second with { Signature = new string('0', second.Signature.Length) };
            await Assert.That((await fixture.Runtime.DiscoverAsync(forged, PeerDiscoveryFixture.Cancellation)).Error).IsEqualTo(ErrorCode.Unauthenticated);
            await Assert.That((await fixture.ReceiveAsync(first)).Error).IsEqualTo(ErrorCode.Unauthenticated);
            await Assert.That((await fixture.ReceiveAsync(second)).Error).IsEqualTo(ErrorCode.ResourceExhausted);
        });
    }

    [Test]
    public async Task ConcurrentActualNativeRequestsRespectFixedReplayCapacity()
    {
        await PeerDiscoveryFixture.RunAsync(cluster, async fixture =>
        {
            var requests = Enumerable.Range(0, 8).Select(_ => fixture.Sign()).ToArray();
            var calls = requests.Select(request => Task.Run(() => fixture.ReceiveAsync(request), PeerDiscoveryFixture.Cancellation));
            var replies = await Task.WhenAll(calls);
            await Assert.That(replies.Count(reply => reply.Error is null)).IsEqualTo(2);
            await Assert.That(replies.Where(reply => reply.Error is not null).All(reply => reply.Error == ErrorCode.ResourceExhausted)).IsTrue();
        }, 2);
    }

    internal static ReplicaNativeDiscoveryRequest Resign(PeerDiscoveryFixture fixture, ReplicaNativeDiscoveryRequest request,
        string purpose = ReplicaNativeDiscoveryProtocol.RequestPurpose)
    {
        var unsigned = request with { Signature = string.Empty };
        var frame = NativeSerialization.Serialize(new ReplicaNativeDiscoveryMacFrame(purpose,
            SHA256.HashData(NativeSerialization.Serialize(unsigned))));
        return request with { Signature = fixture.Sender.SignDiscovery(frame, request.Nonce) };
    }
}

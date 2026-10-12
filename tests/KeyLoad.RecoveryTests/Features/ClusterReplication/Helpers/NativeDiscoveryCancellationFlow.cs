using KeyLoad.Orleans;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.RecoveryTests;

internal static class NativeDiscoveryCancellationFlow
{
    private const int OneAdmission = 1;
    private const int OriginalAndHealthyNonces = 2;

    internal static async Task RunAsync(PeerDiscoveryNativeCluster cluster, PeerDiscoveryFixture fixture)
    {
        var token = PeerDiscoveryFixture.Cancellation;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        var gate = new NativeDiscoveryCancellationGate();
        var settings = new OrleansMembershipOptions
        { MaximumAdmissions = OneAdmission, ReplayNonceCapacity = OriginalAndHealthyNonces };
        var runtime = new ReplicaNativeDiscoveryRuntime(RecoveryExecutionOptions.Configuration(cluster.Receiver.Configuration),
            cluster.Receiver.Discovery, fixture.Receiver, cluster.ReceiverServices.GetRequiredService<ISiloStatusOracle>(),
            Options.Create(settings), TimeProvider.System, gate);
        List<Exception> failures = [];
        Task<ReplicaNativeDiscoveryReply>? original = null;
        try
        {
            await ReplicaMaterializerLifecycleErrors.AttemptAsync(async () =>
        {
            var before = runtime.ReadActiveMembership();
            var expected = cluster.Receiver.Discovery.Read();
            var request = fixture.Sign();
            original = runtime.DiscoverAsync(request, caller.Token);
            await gate.Entered.WaitAsync(token);
            await caller.CancelAsync();
            var refusal = fixture.Sender.VerifyNativeDiscoveryReply(request, await original.WaitAsync(token));
            await Assert.That(refusal.Nonce).IsEqualTo(request.Nonce);
            await Assert.That(refusal.Error).IsEqualTo(ErrorCode.OwnershipLost);
            await Assert.That(refusal.Payload.IsEmpty).IsTrue();
            await Assert.That(refusal.DiscoverySignature).IsEqualTo(string.Empty);
            gate.Release();
            await HealthyAsync(runtime, fixture, expected, token);
            await Assert.That(runtime.ReadActiveMembership().SequenceEqual(before)).IsTrue();
            var stop = runtime.StopAsync();
            await Assert.That(ReferenceEquals(stop, runtime.StopAsync())).IsTrue();
            await stop;
            await runtime.DisposeAsync();
            Assert.ThrowsExactly<ObjectDisposedException>(() => runtime.ReadActiveMembership());
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => runtime.DiscoverAsync(fixture.Sign(), token));
            await Assert.That((await fixture.ReceiveNativeAsync(fixture.Sign())).Error).IsNull();
            }, failures);
        }
        finally
        {
            gate.Release();
            await ReplicaMaterializerLifecycleErrors.AttemptAsync(runtime.StopAsync, failures);
            if (original is not null)
            { await ReplicaMaterializerLifecycleErrors.AttemptAsync(async () => { _ = await original; }, failures); }
            try { await runtime.DisposeAsync(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
        }
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }

    private static async Task HealthyAsync(ReplicaNativeDiscoveryRuntime runtime, PeerDiscoveryFixture fixture,
        ReplicaSiloDiscovery expected, CancellationToken token)
    {
        var request = fixture.Sign();
        var reply = fixture.Sender.VerifyNativeDiscoveryReply(request, await runtime.DiscoverAsync(request, token));
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.Nonce).IsEqualTo(request.Nonce);
        await Assert.That(reply.Payload.IsEmpty).IsFalse();
        var actual = NativeSerialization.Deserialize<ReplicaSiloDiscovery>(reply.Payload.Span);
        await Assert.That(actual).IsEqualTo(expected);
        var replay = fixture.Sender.VerifyNativeDiscoveryReply(request, await runtime.DiscoverAsync(request, token));
        await Assert.That(replay.Error).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(replay.Nonce).IsEqualTo(request.Nonce);
        await Assert.That(replay.Payload.IsEmpty).IsTrue();
        await Assert.That(replay.DiscoverySignature).IsEqualTo(string.Empty);
    }
}

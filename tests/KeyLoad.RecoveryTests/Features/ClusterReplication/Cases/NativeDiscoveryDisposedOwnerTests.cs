using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests;

[PeerDiscoveryDataSource]
internal sealed class NativeDiscoveryDisposedOwnerTests(PeerDiscoveryNativeCluster cluster)
{
    [Test]
    public async Task ActualDisposedNativeSignerAndVerifierRefuseEveryDiscoveryEntry()
    {
        await PeerDiscoveryFixture.RunAsync(cluster, async fixture =>
        {
            var request = fixture.Sign();
            var reply = await fixture.ReceiveNativeAsync(request);
            fixture.Receiver.VerifyNativeDiscoveryRequest(request);
            fixture.Sender.VerifyNativeDiscoveryReply(request, reply);
            var nonce = Guid.NewGuid();
            var originalSignature = fixture.Sender.SignDiscovery(reply.Payload.Span, nonce);
            fixture.Sender.VerifyDiscovery(PeerDiscoveryNativeCluster.SenderVoter, reply.Payload.Span, nonce, originalSignature);
            fixture.Sender.Dispose();
            fixture.Receiver.Dispose();
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Sign());
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Sender.SignDiscovery(reply.Payload.Span, nonce));
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Sender.VerifyDiscovery(
                PeerDiscoveryNativeCluster.SenderVoter, reply.Payload.Span, nonce, originalSignature));
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Receiver.VerifyNativeDiscoveryRequest(request));
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Receiver.CreateNativeDiscoveryReply(request, reply.Payload));
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Sender.VerifyNativeDiscoveryReply(request, reply));
            await fixture.Runtime.StopAsync();
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => fixture.Runtime.DiscoverAsync(request, PeerDiscoveryFixture.Cancellation));
        });
    }

    [Test]
    public async Task ActualDisposedNativeConsensusEnvelopeSigningAndVerificationRefuse()
    {
        await PeerDiscoveryFixture.RunAsync(cluster, async fixture =>
        {
            var native = fixture.Sign();
            var request = fixture.Sender.SignRequest(native.DestinationVoter, ReplicaRpc.ReadBarrier,
                native.DestinationSiloAddress, ReadOnlyMemory<byte>.Empty);
            var reply = fixture.Receiver.CreateReply(request, ReadOnlyMemory<byte>.Empty, ErrorCode.OwnershipLost);
            fixture.Sender.VerifyReply(request, reply);
            fixture.Sender.Dispose();
            fixture.Receiver.Dispose();
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Sender.SignRequest(native.DestinationVoter,
                ReplicaRpc.ReadBarrier, native.DestinationSiloAddress, ReadOnlyMemory<byte>.Empty));
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Receiver.VerifyRequest(request));
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Receiver.ClassifyBorrowedRequest(request));
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Receiver.CreateReply(request, ReadOnlyMemory<byte>.Empty));
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Sender.VerifyReply(request, reply));
        });
    }

    [Test]
    public async Task ConcurrentOriginalNativeMacAndDisposeCannotSignWithAnErasedKey()
    {
        await PeerDiscoveryFixture.RunAsync(cluster, async fixture =>
        {
            var original = await fixture.ReceiveNativeAsync(fixture.Sign());
            var nonce = Guid.NewGuid();
            var expected = fixture.Sender.SignDiscovery(original.Payload.Span, nonce);
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
            {
                await begin.Task;
                try
                {
                    return fixture.Sender.SignDiscovery(original.Payload.Span, nonce);
                }
                catch (ObjectDisposedException) { return null; }
            }, PeerDiscoveryFixture.Cancellation)).ToArray();
            var close = Task.Run(async () =>
            {
                await begin.Task;
                fixture.Sender.Dispose();
            }, PeerDiscoveryFixture.Cancellation);
            begin.TrySetResult();
            string?[] results = [];
            List<Exception> failures = [];
            await ReplicaMaterializerLifecycleErrors.AttemptAsync(async () => results = await Task.WhenAll(calls), failures);
            await ReplicaMaterializerLifecycleErrors.AttemptAsync(() => close, failures);
            ReplicaMaterializerLifecycleErrors.Throw(failures);
            await Assert.That(results.All(signature => signature is null || signature == expected)).IsTrue();
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Sender.SignDiscovery(original.Payload.Span, nonce));
            await Assert.That(fixture.Secret.SequenceEqual(cluster.Sender.Peers.Secret.ToArray())).IsTrue();
        });
    }
}

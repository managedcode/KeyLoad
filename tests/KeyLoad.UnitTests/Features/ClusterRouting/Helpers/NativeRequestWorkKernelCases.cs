using KeyLoad.Orleans;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeRequestWorkKernelCases
{
    private const int SettlementSeconds = 10;
    private static readonly TimeSpan SettlementBound = TimeSpan.FromSeconds(SettlementSeconds);

    internal static async Task AssertLazyStreamAsync(RequestCqrsClusterFixture fixture)
    {
        var owner = new NativeRequestWorkOwner(UnitRoutingOptions.Routing());
        var requestId = Guid.NewGuid();
        var probe = new NativeRequestWorkProbe();
        var enumerator = CreateStream(fixture, owner, requestId, probe).GetAsyncEnumerator();
        var leases = new List<IDisposable>();
        await NativeCqrsTestSupport.RunWithCleanupAsync(async () =>
        {
            leases.Add(owner.Acquire(requestId, NativeRequestWorkKind.RequestProducer));
            await Assert.That(probe.HandlerEntered.IsCompleted).IsFalse();
        }, async () =>
        {
            await NativeRequestWorkKernelCleanup.CleanupAsync(
                owner, probe, enumerator, null, null, null, new HashSet<Task>(), leases);
            await Assert.That(owner.IsJoined).IsTrue();
        });
    }

    internal static async Task AssertHeldProducerJoinAsync(RequestCqrsClusterFixture fixture)
    {
        var owner = new NativeRequestWorkOwner(UnitRoutingOptions.Routing());
        var requestId = Guid.NewGuid();
        var probe = new NativeRequestWorkProbe();
        var enumerator = CreateStream(fixture, owner, requestId, probe).GetAsyncEnumerator();
        Task<bool>? firstMove = null;
        Task? disposal = null;
        Task? drain = null;
        var observed = new HashSet<Task>();
        await NativeCqrsTestSupport.RunWithCleanupAsync(async () =>
        {
            var move = enumerator.MoveNextAsync().AsTask();
            firstMove = move;
            await Assert.That(await move.WaitAsync(SettlementBound)).IsTrue();
            observed.Add(move);
            await Assert.That(enumerator.Current.Kind).IsEqualTo(CqrsStreamChunkKind.Started);
            await probe.HandlerEntered.WaitAsync(SettlementBound);

            var drainTask = owner.DrainAsync();
            drain = drainTask;
            await probe.FinallyEntered.WaitAsync(SettlementBound);
            var disposalTask = enumerator.DisposeAsync().AsTask();
            disposal = disposalTask;
            await NativeRequestWorkKernelAssertions.AssertBothRunningAsync(disposalTask, drainTask);
            await Assert.That(owner.ShutdownToken.IsCancellationRequested).IsTrue();
            await Assert.That(probe.ProducerSettled.IsCompleted).IsFalse();

            probe.ReleaseProducer();
            await probe.ProducerSettled.WaitAsync(SettlementBound);
            await disposalTask.WaitAsync(SettlementBound);
            observed.Add(disposalTask);
            await probe.ActivationSettled.WaitAsync(SettlementBound);
            await drainTask.WaitAsync(SettlementBound);
            observed.Add(drainTask);
            await NativeRequestWorkKernelAssertions.AssertSettlementOrderAsync(probe);
            await Assert.That(owner.IsJoined).IsTrue();
        }, () => NativeRequestWorkKernelCleanup.CleanupAsync(
            owner, probe, enumerator, firstMove, disposal, drain, observed));
    }

    internal static async Task AssertCallbackFailureJoinAsync(RequestCqrsClusterFixture fixture)
    {
        var owner = new NativeRequestWorkOwner(UnitRoutingOptions.Routing());
        var requestId = Guid.NewGuid();
        var probe = new NativeRequestWorkProbe();
        var enumerator = CreateStream(fixture, owner, requestId, probe, throwOnCancellation: true)
            .GetAsyncEnumerator();
        Task<bool>? firstMove = null;
        Task? disposal = null;
        Task? drain = null;
        var observed = new HashSet<Task>();
        var failures = new List<Exception>();
        await NativeCqrsTestSupport.RunWithCleanupAsync(async () =>
        {
            var move = enumerator.MoveNextAsync().AsTask();
            firstMove = move;
            await Assert.That(await move.WaitAsync(SettlementBound)).IsTrue();
            observed.Add(move);
            await Assert.That(enumerator.Current.Kind).IsEqualTo(CqrsStreamChunkKind.Started);
            await probe.CallbackRegistered.WaitAsync(SettlementBound);

            var drainTask = owner.DrainAsync();
            drain = drainTask;
            var disposalTask = enumerator.DisposeAsync().AsTask();
            disposal = disposalTask;
            await probe.CallbackEntered.WaitAsync(SettlementBound);
            await probe.FinallyEntered.WaitAsync(SettlementBound);
            await NativeRequestWorkKernelAssertions.AssertBothRunningAsync(disposalTask, drainTask);
            await Assert.That(probe.ProducerSettled.IsCompleted).IsFalse();
            probe.ReleaseProducer();
            await probe.ProducerSettled.WaitAsync(SettlementBound);
            await NativeRequestWorkKernelCleanup.ObserveBoundedAsync(disposalTask, failures, observed, SettlementBound);
            await NativeRequestWorkKernelCleanup.ObserveBoundedAsync(drainTask, failures, observed, SettlementBound);
            await probe.ActivationSettled.WaitAsync(SettlementBound);
            await NativeRequestWorkKernelCleanup.ObserveBoundedAsync(
                owner.DisposeAsync().AsTask(), failures, observed, SettlementBound);

            await NativeRequestWorkKernelCleanup.AssertContainsFailureAsync(failures, probe.CallbackFailure);
            await NativeRequestWorkKernelAssertions.AssertSettlementOrderAsync(probe);
            await Assert.That(owner.IsJoined).IsTrue();
            await NativeRequestWorkKernelAssertions.AssertHealthyFollowingStreamAsync(fixture);
        }, () => NativeRequestWorkKernelCleanup.CleanupAsync(
            owner, probe, enumerator, firstMove, disposal, drain, observed));
    }

    internal static async Task AssertRejectedAdmissionSettlesAsync(RequestCqrsClusterFixture fixture)
    {
        var owner = new NativeRequestWorkOwner(UnitRoutingOptions.Routing());
        var held = new List<IDisposable>();
        var requestId = Guid.NewGuid();
        var probe = new NativeRequestWorkProbe();
        var enumerator = CreateStream(fixture, owner, requestId, probe).GetAsyncEnumerator();
        Task<bool>? firstMove = null;
        var observed = new HashSet<Task>();
        await NativeCqrsTestSupport.RunWithCleanupAsync(async () =>
        {
            for (var index = 0; index < NativeRequestWorkTestLimits.RequestProducerCapacity; index++)
            {
                held.Add(owner.Acquire(Guid.NewGuid(), NativeRequestWorkKind.RequestProducer));
            }

            var move = enumerator.MoveNextAsync().AsTask();
            firstMove = move;
            var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(
                () => move.WaitAsync(SettlementBound)))!;
            observed.Add(move);
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
            await probe.ActivationSettled.WaitAsync(SettlementBound);
            await Assert.That(probe.HandlerEntered.IsCompleted).IsFalse();
            await Assert.That(probe.ProducerSettled.IsCompleted).IsFalse();
            await Assert.That(probe.ActivationSettleCount).IsEqualTo(1);
        }, () => NativeRequestWorkKernelCleanup.CleanupAsync(
            owner, probe, enumerator, firstMove, null, null, observed, held));
    }

    internal static IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> CreateStream(
        RequestCqrsClusterFixture fixture, NativeRequestWorkOwner owner, Guid requestId,
        NativeRequestWorkProbe probe, bool throwOnCancellation = false)
    {
        var serializer = fixture.Cluster.ServiceProvider.GetRequiredService<
            Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>();
        return NativeCqrsStreamLifetime.Run(token => CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
                writer => probe.HoldAsync(writer, requestId, throwOnCancellation), token), serializer, requestId, TimeProvider.System, probe.SettleActivation, options: fixture.RoutingOptions, owner: owner, cancellationToken: CancellationToken.None);
    }

}

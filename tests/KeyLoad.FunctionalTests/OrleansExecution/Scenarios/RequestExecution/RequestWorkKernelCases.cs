using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class RequestWorkKernelCases
{
    private const int SettlementSeconds = 10;
    private static readonly TimeSpan SettlementBound = TimeSpan.FromSeconds(SettlementSeconds);

    internal static async Task AssertLazyStreamAsync(RequestCqrsHostedRuntimeFixture fixture)
    {
        var owner = new RequestWorkOwner();
        var requestId = Guid.NewGuid();
        var probe = new RequestWorkProbe();
        var enumerator = CreateStream(fixture, owner, requestId, probe).GetAsyncEnumerator();
        var leases = new List<IDisposable>();
        await CqrsTestSupport.RunWithCleanupAsync(async () =>
        {
            leases.Add(owner.Acquire(requestId, RequestWorkKind.RequestProducer));
            await Assert.That(probe.HandlerEntered.IsCompleted).IsFalse();
        }, async () =>
        {
            await RequestWorkKernelCleanup.CleanupAsync(
                owner, probe, enumerator, null, null, null, new HashSet<Task>(), leases);
            await Assert.That(owner.IsJoined).IsTrue();
        });
    }

    internal static async Task AssertHeldProducerJoinAsync(RequestCqrsHostedRuntimeFixture fixture)
    {
        var owner = new RequestWorkOwner();
        var requestId = Guid.NewGuid();
        var probe = new RequestWorkProbe();
        var enumerator = CreateStream(fixture, owner, requestId, probe).GetAsyncEnumerator();
        Task<bool>? firstMove = null;
        Task? disposal = null;
        Task? drain = null;
        var observed = new HashSet<Task>();
        await CqrsTestSupport.RunWithCleanupAsync(async () =>
        {
            var move = enumerator.MoveNextAsync().AsTask();
            firstMove = move;
            await Assert.That(await move.WaitAsync(SettlementBound, TimeProvider.System)).IsTrue();
            observed.Add(move);
            await Assert.That(enumerator.Current.Kind).IsEqualTo(CqrsStreamChunkKind.Started);
            await probe.HandlerEntered.WaitAsync(SettlementBound, TimeProvider.System);

            var drainTask = owner.DrainAsync();
            drain = drainTask;
            await probe.FinallyEntered.WaitAsync(SettlementBound, TimeProvider.System);
            var disposalTask = enumerator.DisposeAsync().AsTask();
            disposal = disposalTask;
            await RequestWorkKernelAssertions.AssertBothRunningAsync(disposalTask, drainTask);
            await Assert.That(owner.ShutdownToken.IsCancellationRequested).IsTrue();
            await Assert.That(probe.ProducerSettled.IsCompleted).IsFalse();

            probe.ReleaseProducer();
            await probe.ProducerSettled.WaitAsync(SettlementBound, TimeProvider.System);
            await disposalTask.WaitAsync(SettlementBound, TimeProvider.System);
            observed.Add(disposalTask);
            await probe.ActivationSettled.WaitAsync(SettlementBound, TimeProvider.System);
            await drainTask.WaitAsync(SettlementBound, TimeProvider.System);
            observed.Add(drainTask);
            await RequestWorkKernelAssertions.AssertSettlementOrderAsync(probe);
            await Assert.That(owner.IsJoined).IsTrue();
        }, () => RequestWorkKernelCleanup.CleanupAsync(
            owner, probe, enumerator, firstMove, disposal, drain, observed));
    }

    internal static async Task AssertCallbackFailureJoinAsync(RequestCqrsHostedRuntimeFixture fixture)
    {
        var owner = new RequestWorkOwner();
        var requestId = Guid.NewGuid();
        var probe = new RequestWorkProbe();
        var enumerator = CreateStream(fixture, owner, requestId, probe, throwOnCancellation: true)
            .GetAsyncEnumerator();
        Task<bool>? firstMove = null;
        Task? disposal = null;
        Task? drain = null;
        var observed = new HashSet<Task>();
        var failures = new List<Exception>();
        await CqrsTestSupport.RunWithCleanupAsync(async () =>
        {
            var move = enumerator.MoveNextAsync().AsTask();
            firstMove = move;
            await Assert.That(await move.WaitAsync(SettlementBound, TimeProvider.System)).IsTrue();
            observed.Add(move);
            await Assert.That(enumerator.Current.Kind).IsEqualTo(CqrsStreamChunkKind.Started);
            await probe.CallbackRegistered.WaitAsync(SettlementBound, TimeProvider.System);

            var drainTask = owner.DrainAsync();
            drain = drainTask;
            var disposalTask = enumerator.DisposeAsync().AsTask();
            disposal = disposalTask;
            await probe.CallbackEntered.WaitAsync(SettlementBound, TimeProvider.System);
            await probe.FinallyEntered.WaitAsync(SettlementBound, TimeProvider.System);
            await RequestWorkKernelAssertions.AssertBothRunningAsync(disposalTask, drainTask);
            await Assert.That(probe.ProducerSettled.IsCompleted).IsFalse();
            probe.ReleaseProducer();
            await probe.ProducerSettled.WaitAsync(SettlementBound, TimeProvider.System);
            await RequestWorkKernelCleanup.ObserveBoundedAsync(disposalTask, failures, observed, SettlementBound);
            await RequestWorkKernelCleanup.ObserveBoundedAsync(drainTask, failures, observed, SettlementBound);
            await probe.ActivationSettled.WaitAsync(SettlementBound, TimeProvider.System);
            await RequestWorkKernelCleanup.ObserveBoundedAsync(
                owner.DisposeAsync().AsTask(), failures, observed, SettlementBound);

            await RequestWorkKernelCleanup.AssertContainsFailureAsync(failures, probe.CallbackFailure);
            await RequestWorkKernelAssertions.AssertSettlementOrderAsync(probe);
            await Assert.That(owner.IsJoined).IsTrue();
            await RequestWorkKernelAssertions.AssertHealthyFollowingStreamAsync(fixture);
        }, () => RequestWorkKernelCleanup.CleanupAsync(
            owner, probe, enumerator, firstMove, disposal, drain, observed));
    }

    internal static async Task AssertDuplicateIdentitySettlesAsync(RequestCqrsHostedRuntimeFixture fixture)
    {
        var owner = new RequestWorkOwner();
        var held = new List<IDisposable>();
        var requestId = Guid.NewGuid();
        var probe = new RequestWorkProbe();
        var enumerator = CreateStream(fixture, owner, requestId, probe).GetAsyncEnumerator();
        Task<bool>? firstMove = null;
        var observed = new HashSet<Task>();
        await CqrsTestSupport.RunWithCleanupAsync(async () =>
        {
            held.Add(owner.Acquire(requestId, RequestWorkKind.RequestProducer));

            var move = enumerator.MoveNextAsync().AsTask();
            firstMove = move;
            var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(
                () => move.WaitAsync(SettlementBound, TimeProvider.System)))!;
            observed.Add(move);
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Conflict);
            await probe.ActivationSettled.WaitAsync(SettlementBound, TimeProvider.System);
            await Assert.That(probe.HandlerEntered.IsCompleted).IsFalse();
            await Assert.That(probe.ProducerSettled.IsCompleted).IsFalse();
            await Assert.That(probe.ActivationSettleCount).IsEqualTo(1);
        }, () => RequestWorkKernelCleanup.CleanupAsync(
            owner, probe, enumerator, firstMove, null, null, observed, held));
    }

    internal static IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> CreateStream(
        RequestCqrsHostedRuntimeFixture fixture, RequestWorkOwner owner, Guid requestId,
        RequestWorkProbe probe, bool throwOnCancellation = false)
    {
        var serializer = fixture.Services.GetRequiredService<
            Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>();
        return CqrsStreamLifetime.Run(token => CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
                writer => probe.HoldAsync(writer, requestId, throwOnCancellation), token), serializer, requestId, TimeProvider.System, probe.SettleActivation, options: fixture.RoutingOptions, owner: owner, cancellationToken: CancellationToken.None);
    }

}

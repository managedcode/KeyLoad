using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NativeCqrsDataSource]
internal sealed class NativeCqrsLifetimeTests(NativeCqrsClusterFixture fixture)
{
    private static readonly TimeSpan CleanupBound = TimeSpan.FromSeconds(10);

    [Test]
    public async Task CapacityOneProducerBlocksAndCancellationSettlesBeforeNextStream()
    {
        var requestId = NativeCqrsRequestIds.Cancellation;
        using var cancellation = new CancellationTokenSource();
        var source = fixture.GrainFactory.GetGrain<INativeCqrsStreamGrain>(requestId);
        var enumerator = source.BackpressureAsync(requestId, cancellation.Token)
            .WithBatchSize(NativeCqrsProtocol.BatchSize)
            .GetAsyncEnumerator(cancellation.Token);
        Task? disposal = null;
        Task<bool>? initialMove = null;
        await NativeCqrsTestSupport.RunWithCleanupAsync(async () =>
        {
            initialMove = enumerator.MoveNextAsync().AsTask();
            await Assert.That(await initialMove.WaitAsync(CleanupBound, TimeProvider.System)).IsTrue();
            await Assert.That(enumerator.Current.Kind).IsEqualTo(CqrsStreamChunkKind.Started);
            var attempted = await NativeCqrsTestSupport.WaitForEventAsync(
                fixture.GrainFactory, requestId, NativeCqrsProtocol.ProgressTwoAttempted);
            await Assert.That(attempted.Events).Contains(NativeCqrsProtocol.ProgressOne);
            await Assert.That(attempted.Events).DoesNotContain(NativeCqrsProtocol.ProgressTwo);
            await Assert.That(attempted.Settled).IsFalse();

            await cancellation.CancelAsync();
            disposal = enumerator.DisposeAsync().AsTask();
            await disposal.WaitAsync(CleanupBound, TimeProvider.System);
            var settled = await NativeCqrsTestSupport.WaitForEventAsync(
                fixture.GrainFactory, requestId, NativeCqrsProtocol.Settled);
            await Assert.That(settled.Settled).IsTrue();
            await Assert.That(settled.CancellationObserved).IsTrue();
            await Assert.That(settled.SettlementCount).IsEqualTo(NativeCqrsProtocol.ExpectedSettlementCount);
            await Assert.That(settled.HandlerOutcomeReturned).IsFalse();
            await Assert.That(settled.HandlerOutcomeSucceeded).IsFalse();
            await AssertHealthyFollowingStreamAsync(NativeCqrsRequestIds.HealthyAfterCancellation);
        }, () => FinishAsync(enumerator, cancellation, disposal, initialMove, requestId));
    }

    [Test]
    public async Task EarlyEnumeratorDisposalSettlesProducerAndLeavesNextStreamHealthy()
    {
        var requestId = NativeCqrsRequestIds.Disposal;
        using var cancellation = new CancellationTokenSource();
        var source = fixture.GrainFactory.GetGrain<INativeCqrsStreamGrain>(requestId);
        var enumerator = source.BackpressureAsync(requestId, cancellation.Token)
            .WithBatchSize(NativeCqrsProtocol.BatchSize)
            .GetAsyncEnumerator();
        Task? disposal = null;
        Task<bool>? initialMove = null;
        await NativeCqrsTestSupport.RunWithCleanupAsync(async () =>
        {
            initialMove = enumerator.MoveNextAsync().AsTask();
            await Assert.That(await initialMove.WaitAsync(CleanupBound, TimeProvider.System)).IsTrue();
            await Assert.That(enumerator.Current.Kind).IsEqualTo(CqrsStreamChunkKind.Started);
            var attempted = await NativeCqrsTestSupport.WaitForEventAsync(
                fixture.GrainFactory, requestId, NativeCqrsProtocol.ProgressTwoAttempted);
            await Assert.That(attempted.Events).Contains(NativeCqrsProtocol.ProgressOne);
            await Assert.That(attempted.Events).DoesNotContain(NativeCqrsProtocol.ProgressTwo);
            await Assert.That(attempted.Settled).IsFalse();

            disposal = enumerator.DisposeAsync().AsTask();
            await disposal.WaitAsync(CleanupBound, TimeProvider.System);
            var settled = await NativeCqrsTestSupport.WaitForEventAsync(
                fixture.GrainFactory, requestId, NativeCqrsProtocol.Settled);
            await Assert.That(settled.Settled).IsTrue();
            await Assert.That(settled.CancellationObserved).IsTrue();
            await Assert.That(settled.SettlementCount).IsEqualTo(NativeCqrsProtocol.ExpectedSettlementCount);
            await Assert.That(settled.HandlerOutcomeReturned).IsFalse();
            await Assert.That(settled.HandlerOutcomeSucceeded).IsFalse();
            await AssertHealthyFollowingStreamAsync(NativeCqrsRequestIds.HealthyAfterDisposal);
        }, () => FinishAsync(enumerator, cancellation, disposal, initialMove, requestId));
    }

    private async Task AssertHealthyFollowingStreamAsync(Guid requestId)
    {
        var source = fixture.GrainFactory.GetGrain<INativeCqrsStreamGrain>(requestId);
        var chunks = await NativeCqrsTestSupport.ReadAllAsync(
            source.AllowedAsync(requestId, CancellationToken.None));
        await NativeCqrsTestSupport.AssertTerminalSequenceAsync(chunks, CqrsStreamChunkKind.Completed);
        await Assert.That(chunks.Count).IsEqualTo(NativeCqrsProtocol.ExpectedCompletedChunks);
        await Assert.That(chunks[2].TryGetResult(out var result)).IsTrue();
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.RequestId).IsEqualTo(requestId);
        var settled = await NativeCqrsTestSupport.WaitForEventAsync(
            fixture.GrainFactory, requestId, NativeCqrsProtocol.Settled);
        await Assert.That(settled.HandlerOutcomeReturned).IsTrue();
        await Assert.That(settled.HandlerOutcomeSucceeded).IsTrue();
    }

    private async Task FinishAsync(
        IAsyncEnumerator<CqrsStreamChunk<NativeCqrsProgress, NativeCqrsResult>> enumerator,
        CancellationTokenSource cancellation,
        Task? disposal,
        Task<bool>? initialMove,
        Guid requestId)
    {
        var cancellationTask = CancelAsync(cancellation);
        var disposalTask = DisposeEnumeratorAsync(enumerator, disposal, initialMove, cancellation.Token);
        var settlementTask = AssertProducerSettledAsync(requestId);
        var cleanup = Task.WhenAll(cancellationTask, disposalTask, settlementTask);
        try
        {
            await cleanup;
        }
        catch (Exception) when (cleanup.IsFaulted)
        {
            if (cleanup.Exception is { } failure)
            {
                throw new AggregateException(failure.InnerExceptions);
            }
            throw;
        }
    }

    private static async Task CancelAsync(CancellationTokenSource cancellation)
    {
        await cancellation.CancelAsync();
    }

    private static async Task DisposeEnumeratorAsync(
        IAsyncEnumerator<CqrsStreamChunk<NativeCqrsProgress, NativeCqrsResult>> enumerator,
        Task? existingDisposal,
        Task<bool>? initialMove,
        CancellationToken cancellationToken)
    {
        if (initialMove is not null)
        {
            try
            {
                await initialMove.WaitAsync(CleanupBound, TimeProvider.System, CancellationToken.None);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
        if (existingDisposal is not null)
        {
            await existingDisposal.WaitAsync(CleanupBound, TimeProvider.System, CancellationToken.None);
            return;
        }
        await enumerator.DisposeAsync().AsTask().WaitAsync(CleanupBound, TimeProvider.System, CancellationToken.None);
    }

    private async Task AssertProducerSettledAsync(Guid requestId)
    {
        var settled = await NativeCqrsTestSupport.WaitForEventAsync(
            fixture.GrainFactory, requestId, NativeCqrsProtocol.Settled);
        await Assert.That(settled.Settled).IsTrue();
        await Assert.That(settled.SettlementCount).IsEqualTo(NativeCqrsProtocol.ExpectedSettlementCount);
    }
}

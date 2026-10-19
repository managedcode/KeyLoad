using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

[CqrsDataSource]
internal sealed class CqrsLifetimeTests(CqrsClusterFixture fixture)
{
    private static readonly TimeSpan CleanupBound = TimeSpan.FromSeconds(10);

    [Test]
    public async Task CapacityOneProducerBlocksAndCancellationSettlesBeforeNextStream()
    {
        var requestId = CqrsRequestIds.Cancellation;
        using var cancellation = new CancellationTokenSource();
        var source = fixture.GrainFactory.GetGrain<ICqrsStreamGrain>(requestId);
        var enumerator = source.BackpressureAsync(requestId, cancellation.Token)
            .WithBatchSize(CqrsProtocol.BatchSize)
            .GetAsyncEnumerator(cancellation.Token);
        Task? disposal = null;
        Task<bool>? initialMove = null;
        await CqrsTestSupport.RunWithCleanupAsync(async () =>
        {
            initialMove = enumerator.MoveNextAsync().AsTask();
            await Assert.That(await initialMove.WaitAsync(CleanupBound, TimeProvider.System)).IsTrue();
            await Assert.That(enumerator.Current.Kind).IsEqualTo(CqrsStreamChunkKind.Started);
            var attempted = await CqrsTestSupport.WaitForEventAsync(
                fixture.GrainFactory, requestId, CqrsProtocol.ProgressTwoAttempted);
            await Assert.That(attempted.Events).Contains(CqrsProtocol.ProgressOne);
            await Assert.That(attempted.Events).DoesNotContain(CqrsProtocol.ProgressTwo);
            await Assert.That(attempted.Settled).IsFalse();

            await cancellation.CancelAsync();
            disposal = enumerator.DisposeAsync().AsTask();
            await disposal.WaitAsync(CleanupBound, TimeProvider.System);
            var settled = await CqrsTestSupport.WaitForEventAsync(
                fixture.GrainFactory, requestId, CqrsProtocol.Settled);
            await Assert.That(settled.Settled).IsTrue();
            await Assert.That(settled.CancellationObserved).IsTrue();
            await Assert.That(settled.SettlementCount).IsEqualTo(CqrsProtocol.ExpectedSettlementCount);
            await Assert.That(settled.HandlerOutcomeReturned).IsFalse();
            await Assert.That(settled.HandlerOutcomeSucceeded).IsFalse();
            await AssertHealthyFollowingStreamAsync(CqrsRequestIds.HealthyAfterCancellation);
        }, () => FinishAsync(enumerator, cancellation, disposal, initialMove, requestId));
    }

    [Test]
    public async Task EarlyEnumeratorDisposalSettlesProducerAndLeavesNextStreamHealthy()
    {
        var requestId = CqrsRequestIds.Disposal;
        using var cancellation = new CancellationTokenSource();
        var source = fixture.GrainFactory.GetGrain<ICqrsStreamGrain>(requestId);
        var enumerator = source.BackpressureAsync(requestId, cancellation.Token)
            .WithBatchSize(CqrsProtocol.BatchSize)
            .GetAsyncEnumerator();
        Task? disposal = null;
        Task<bool>? initialMove = null;
        await CqrsTestSupport.RunWithCleanupAsync(async () =>
        {
            initialMove = enumerator.MoveNextAsync().AsTask();
            await Assert.That(await initialMove.WaitAsync(CleanupBound, TimeProvider.System)).IsTrue();
            await Assert.That(enumerator.Current.Kind).IsEqualTo(CqrsStreamChunkKind.Started);
            var attempted = await CqrsTestSupport.WaitForEventAsync(
                fixture.GrainFactory, requestId, CqrsProtocol.ProgressTwoAttempted);
            await Assert.That(attempted.Events).Contains(CqrsProtocol.ProgressOne);
            await Assert.That(attempted.Events).DoesNotContain(CqrsProtocol.ProgressTwo);
            await Assert.That(attempted.Settled).IsFalse();

            disposal = enumerator.DisposeAsync().AsTask();
            await disposal.WaitAsync(CleanupBound, TimeProvider.System);
            var settled = await CqrsTestSupport.WaitForEventAsync(
                fixture.GrainFactory, requestId, CqrsProtocol.Settled);
            await Assert.That(settled.Settled).IsTrue();
            await Assert.That(settled.CancellationObserved).IsTrue();
            await Assert.That(settled.SettlementCount).IsEqualTo(CqrsProtocol.ExpectedSettlementCount);
            await Assert.That(settled.HandlerOutcomeReturned).IsFalse();
            await Assert.That(settled.HandlerOutcomeSucceeded).IsFalse();
            await AssertHealthyFollowingStreamAsync(CqrsRequestIds.HealthyAfterDisposal);
        }, () => FinishAsync(enumerator, cancellation, disposal, initialMove, requestId));
    }

    private async Task AssertHealthyFollowingStreamAsync(Guid requestId)
    {
        var source = fixture.GrainFactory.GetGrain<ICqrsStreamGrain>(requestId);
        var chunks = await CqrsTestSupport.ReadAllAsync(
            source.AllowedAsync(requestId, CancellationToken.None));
        await CqrsTestSupport.AssertTerminalSequenceAsync(chunks, CqrsStreamChunkKind.Completed);
        await Assert.That(chunks.Count).IsEqualTo(CqrsProtocol.ExpectedCompletedChunks);
        await Assert.That(chunks[2].TryGetResult(out var result)).IsTrue();
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.RequestId).IsEqualTo(requestId);
        var settled = await CqrsTestSupport.WaitForEventAsync(
            fixture.GrainFactory, requestId, CqrsProtocol.Settled);
        await Assert.That(settled.HandlerOutcomeReturned).IsTrue();
        await Assert.That(settled.HandlerOutcomeSucceeded).IsTrue();
    }

    private async Task FinishAsync(
        IAsyncEnumerator<CqrsStreamChunk<CqrsProgress, CqrsResult>> enumerator,
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
        IAsyncEnumerator<CqrsStreamChunk<CqrsProgress, CqrsResult>> enumerator,
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
        var settled = await CqrsTestSupport.WaitForEventAsync(
            fixture.GrainFactory, requestId, CqrsProtocol.Settled);
        await Assert.That(settled.Settled).IsTrue();
        await Assert.That(settled.SettlementCount).IsEqualTo(CqrsProtocol.ExpectedSettlementCount);
    }
}

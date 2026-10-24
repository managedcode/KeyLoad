using KeyLoad.UnitTests.TestInfrastructure;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.OrleansExecution;

internal static class CqrsTestSupport
{
    private const int MaximumChunks = 8;
    internal static readonly TimeSpan WaitBound = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);
    private const string WaitExpired = "The execution CQRS actor did not reach the expected state before the test deadline.";

    internal static async Task<List<CqrsStreamChunk<CqrsProgress, CqrsResult>>> ReadAllAsync(
        IAsyncEnumerable<CqrsStreamChunk<CqrsProgress, CqrsResult>> stream,
        CancellationToken cancellationToken = default)
    {
        using var timeoutTimeout = new CancellationTokenSource(WaitBound, TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTimeout.Token);
        var chunks = new List<CqrsStreamChunk<CqrsProgress, CqrsResult>>(4);
        await foreach (var chunk in stream.WithBatchSize(CqrsProtocol.BatchSize)
                           .WithCancellation(timeout.Token))
        {
            chunks.Add(chunk);
            if (chunks.Count > MaximumChunks)
            {
                throw new InvalidOperationException(WaitExpired);
            }
        }
        return chunks;
    }

    internal static async Task<CqrsObservationSnapshot> WaitForEventAsync(
        IGrainFactory grainFactory, Guid requestId, string expectedEvent)
    {
        var started = new TestElapsedClock(TimeProvider.System);
        var reader = grainFactory.GetGrain<ICqrsObservationReaderGrain>(requestId);
        while (true)
        {
            var remaining = Remaining(started);
            var snapshot = await reader.ReadAsync().WaitAsync(remaining, started.Provider);
            if (snapshot.Events.Contains(expectedEvent, StringComparer.Ordinal))
            {
                return snapshot;
            }
            await Task.Delay(PollInterval, started.Provider).WaitAsync(Remaining(started), started.Provider);
        }
    }

    internal static async Task AssertTerminalSequenceAsync(
        IReadOnlyList<CqrsStreamChunk<CqrsProgress, CqrsResult>> chunks,
        CqrsStreamChunkKind expectedTerminal)
    {
        await Assert.That(chunks.Count).IsGreaterThan(0);
        var terminalCount = 0;
        for (var index = 0; index < chunks.Count; index++)
        {
            await Assert.That(chunks[index].Sequence).IsEqualTo(index + CqrsProtocol.FirstSequence);
            if (chunks[index].IsTerminal)
            {
                terminalCount++;
                await Assert.That((index == chunks.Count - 1)).IsTrue();
                await Assert.That(chunks[index].Kind).IsEqualTo(expectedTerminal);
            }
        }
        await Assert.That(terminalCount).IsEqualTo(1);
    }

    internal static async Task RunWithCleanupAsync(Func<Task> body, Func<Task> cleanup)
    {
        try
        {
            await body();
        }
        catch (Exception bodyFailure)
        {
            try
            {
                await cleanup();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(bodyFailure, cleanupFailure);
            }
            throw;
        }
        await cleanup();
    }

    internal static TimeSpan Remaining(TestElapsedClock started)
    {
        var remaining = WaitBound - started.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            throw new TimeoutException(WaitExpired);
        }
        return remaining;
    }
}

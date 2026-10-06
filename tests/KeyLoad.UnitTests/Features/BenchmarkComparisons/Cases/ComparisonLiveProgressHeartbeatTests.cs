using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ComparisonLiveProgressHeartbeatTests
{
    private const int ObservationDeadlineSeconds = 45;

    [Test]
    public async Task AcBcLive003RealHeartbeatWritesLatestStateAndDisposalJoinsActiveOutput()
    {
        using var file = new ComparisonLiveProgressFile();
        using var releaseOutput = new ManualResetEventSlim();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(ObservationDeadlineSeconds));
        var heartbeat = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        void Write(string line)
        {
            File.WriteAllText(file.Path, line);
            if (Interlocked.Increment(ref writes) == 2)
            {
                heartbeat.SetResult(line);
                releaseOutput.Wait(deadline.Token);
            }
        }
        var observer = new ComparisonProgressObserver(Write, UnitBenchmarkOptions.Native());
        Task? disposal = null;
        try
        {
            observer.Begin(ComparisonProgressPhase.Warmup, 1, 2);
            observer.Settle(success: true);
            observer.Settle(success: false);
            var latest = await heartbeat.Task.WaitAsync(deadline.Token);
            await ComparisonLiveProgressAssertions.AssertClosedAsync(latest);
            await Assert.That(latest.Contains("phase=warmup repetition=1 completed=2 total=2 failed=1", StringComparison.Ordinal)).IsTrue();
            disposal = observer.DisposeAsync().AsTask();
            await Assert.That(disposal.IsCompleted).IsFalse();
        }
        finally
        {
            releaseOutput.Set();
            await (disposal ?? observer.DisposeAsync().AsTask()).WaitAsync(deadline.Token);
        }
        await Assert.That(Volatile.Read(ref writes)).IsEqualTo(3);
        await ComparisonLiveProgressAssertions.AssertClosedAsync(await File.ReadAllTextAsync(file.Path, deadline.Token));
    }
}

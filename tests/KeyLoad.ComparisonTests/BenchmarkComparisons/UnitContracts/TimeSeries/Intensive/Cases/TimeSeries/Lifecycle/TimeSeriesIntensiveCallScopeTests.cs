using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveCallScopeTests
{
    private static readonly TimeSpan TestLifetime = TimeSpan.FromSeconds(60);

    [Test]
    public async Task AcTsi004OriginalOnTimeTaskFaultStopsTheClockAndKeepsTheError()
    {
        using var call = new TimeSeriesIntensiveCallScope(UnitBenchmarkOptions.Execution(), TestContext.Current!.Execution.CancellationToken);
        var primary = new InvalidOperationException("private fixture detail");
        var original = Task.FromException(primary);
        call.Start();
        var observed = await ObserveAsync(original, call);
        await Assert.That(observed).IsSameReferenceAs(primary);
        await Assert.That(call.Completion).IsEqualTo(TimeSeriesIntensiveOutcome.Succeeded);
        await Assert.That(call.LatencyTicks).IsGreaterThanOrEqualTo(0L);
        await Assert.That(original.IsFaulted).IsTrue();
    }

    [Test]
    public async Task AcTsi004OriginalTaskOwnDeadlineIsDistinctFromCallerCancellation()
    {
        using var lifetimeTimeout = new CancellationTokenSource(TestLifetime, TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, lifetimeTimeout.Token);
        using var call = new TimeSeriesIntensiveCallScope(UnitBenchmarkOptions.Execution(), lifetime.Token);
        call.Start();
        var original = Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, call.Token);
        var observed = await ObserveAsync(original, call);
        await Assert.That(original.IsCanceled).IsTrue();
        await Assert.That(lifetime.IsCancellationRequested).IsFalse();
        await Assert.That(call.Completion).IsEqualTo(TimeSeriesIntensiveOutcome.DeadlineExceeded);
        await Assert.That(TimeSeriesIntensiveFailure.Capture(observed).Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.DeadlineExceeded);
        await Assert.That(observed.InnerException is OperationCanceledException).IsTrue();
    }

    [Test]
    public async Task AcTsi004OriginalTaskCallerCancellationKeepsTheObservedCause()
    {
        using var cell = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        using var call = new TimeSeriesIntensiveCallScope(UnitBenchmarkOptions.Execution(), cell.Token);
        call.Start();
        var original = Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, call.Token);
        await cell.CancelAsync();
        var observed = await ObserveAsync(original, call);
        await Assert.That(original.IsCanceled).IsTrue();
        await Assert.That(call.Completion).IsEqualTo(TimeSeriesIntensiveOutcome.Cancelled);
        await Assert.That(TimeSeriesIntensiveFailure.Capture(observed).Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.Cancelled);
        await Assert.That(observed.InnerException is OperationCanceledException).IsTrue();
    }

    [Test]
    public async Task AcTsi004AlreadyCancelledEntryLeavesTheClockAndOutcomeUnstarted()
    {
        using var cell = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        await cell.CancelAsync();
        using var call = new TimeSeriesIntensiveCallScope(UnitBenchmarkOptions.Execution(), cell.Token);
        await Assert.That(call.AcceptInvocation()).IsFalse();
        await Assert.That(call.LatencyTicks).IsEqualTo(0L);
        await Assert.That(call.Completion).IsEqualTo(TimeSeriesIntensiveOutcome.NotStarted);
    }

    [Test]
    public async Task AcTsi004CancellationAfterAcceptedEntryRecordsAnOriginalCallerTask()
    {
        using var cell = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        using var call = new TimeSeriesIntensiveCallScope(UnitBenchmarkOptions.Execution(), cell.Token);
        await Assert.That(call.AcceptInvocation()).IsTrue();
        await cell.CancelAsync();
        call.Start();
        var original = Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, call.Token);
        var observed = await ObserveAsync(original, call);
        await Assert.That(original.IsCanceled).IsTrue();
        await Assert.That(call.Completion).IsEqualTo(TimeSeriesIntensiveOutcome.Cancelled);
        await Assert.That(TimeSeriesIntensiveFailure.Capture(observed).Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.Cancelled);
    }

    private static async Task<Exception> ObserveAsync(Task original, TimeSeriesIntensiveCallScope call)
    {
        try
        {
            await original;
        }
        catch (OperationCanceledException error)
        {
            return call.ObserveFailure(error);
        }
        catch (InvalidOperationException error)
        {
            return call.ObserveFailure(error);
        }

        throw new InvalidOperationException("The original semantic task must fault or cancel.");
    }
}

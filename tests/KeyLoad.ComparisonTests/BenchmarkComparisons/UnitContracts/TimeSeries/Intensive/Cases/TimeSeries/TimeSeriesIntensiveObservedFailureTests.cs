using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using Npgsql;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveObservedFailureTests
{
    [Test]
    public async Task AcTsi004OnTimeFailureKeepsTheOriginalExceptionAndFacts()
    {
        foreach (var original in NativeErrors())
        {
            var observed = TimeSeriesIntensiveObservedFailureException.Observe(TimeSeriesIntensiveOutcome.Succeeded, original);
            await Assert.That(observed).IsSameReferenceAs(original);
            await Assert.That(TimeSeriesIntensiveFailure.Capture(observed)).IsEqualTo(TimeSeriesIntensiveFailure.Capture(original));
        }
    }

    [Test]
    public async Task AcTsi004ObservedDeadlineKeepsTheOriginalNativeFailureFacts()
    {
        foreach (var original in NativeErrors())
        {
            var observed = TimeSeriesIntensiveObservedFailureException.Observe(TimeSeriesIntensiveOutcome.DeadlineExceeded, original);
            await Assert.That(observed.InnerException).IsSameReferenceAs(original);
            var captured = TimeSeriesIntensiveFailure.Capture(observed);
            await Assert.That(captured.Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.DeadlineExceeded);
            await Assert.That(captured.Failure).IsEqualTo(TimeSeriesIntensiveFailure.From(original));
        }
    }

    [Test]
    public async Task AcTsi004ObservedCallerCancellationKeepsTheOriginalNativeFacts()
    {
        foreach (var original in NativeErrors())
        {
            var observed = TimeSeriesIntensiveObservedFailureException.Observe(TimeSeriesIntensiveOutcome.Cancelled, original);
            await Assert.That(observed.InnerException).IsSameReferenceAs(original);
            var captured = TimeSeriesIntensiveFailure.Capture(observed);
            await Assert.That(captured.Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.Cancelled);
            await Assert.That(captured.Failure).IsEqualTo(TimeSeriesIntensiveFailure.From(original));
        }
    }

    [Test]
    public async Task AcTsi004FatalOriginalCausesCannotBeReplacedByObservedFailure()
    {
        foreach (var type in new[] { typeof(OutOfMemoryException), typeof(StackOverflowException), typeof(AccessViolationException) })
        {
            var original = FatalInput(type);
            var wrapped = new AggregateException(new InvalidOperationException(), original);
            await Assert.That(TimeSeriesIntensiveObservedFailureException.Observe(TimeSeriesIntensiveOutcome.DeadlineExceeded, original))
                .IsSameReferenceAs(original);
            await Assert.That(TimeSeriesIntensiveObservedFailureException.Observe(TimeSeriesIntensiveOutcome.Cancelled, wrapped))
                .IsSameReferenceAs(wrapped);
            await Assert.That(TimeSeriesIntensiveExceptionBoundary.IsNonfatal(wrapped)).IsFalse();
        }
    }

    [Test]
    public void AcTsi004ObservedFailureRejectsOutcomesOutsideTheCompletionBoundary()
    {
        foreach (var outcome in new[] { TimeSeriesIntensiveOutcome.NotStarted, TimeSeriesIntensiveOutcome.TargetFailure,
            TimeSeriesIntensiveOutcome.ValidationFailure, (TimeSeriesIntensiveOutcome)int.MaxValue })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                TimeSeriesIntensiveObservedFailureException.Observe(outcome, new InvalidOperationException()));
        }
    }

    [Test]
    public async Task AcTsi004NestedObservationKeepsTheActualNativeCodeAndLatestOutcome()
    {
        var original = new KeyLoadException(ErrorCode.UnknownWriteOutcome, "private fixture detail", 503);
        var deadline = TimeSeriesIntensiveObservedFailureException.Observe(TimeSeriesIntensiveOutcome.DeadlineExceeded, original);
        var cancelled = TimeSeriesIntensiveObservedFailureException.Observe(TimeSeriesIntensiveOutcome.Cancelled, deadline);
        var captured = TimeSeriesIntensiveFailure.Capture(cancelled);
        await Assert.That(captured.Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.Cancelled);
        await Assert.That(captured.Failure).IsEqualTo(new TimeSeriesIntensiveFailure(
            TimeSeriesIntensiveFailureOrigin.KeyLoad, ErrorCode.UnknownWriteOutcome, 503, null));
    }

    [Test]
    public async Task AcTsi004StandardConstructionRemainsUnexpectedWithoutInventedNativeFacts()
    {
        foreach (var original in new[] { new TimeSeriesIntensiveObservedFailureException(),
            new TimeSeriesIntensiveObservedFailureException("private fixture detail"),
            new TimeSeriesIntensiveObservedFailureException("private fixture detail", new InvalidOperationException()) })
        {
            var captured = TimeSeriesIntensiveFailure.Capture(original);
            await Assert.That(captured.Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.UnexpectedFailure);
            await Assert.That(captured.Failure).IsEqualTo(new TimeSeriesIntensiveFailure(
                TimeSeriesIntensiveFailureOrigin.Unexpected, null, null, null));
        }
    }

    [Test]
    public async Task AcTsi004MalformedNativeCodeCannotEraseTheObservedDeadline()
    {
        var original = new PostgresException("private fixture detail", "ERROR", "ERROR", "xx000");
        var observed = TimeSeriesIntensiveObservedFailureException.Observe(TimeSeriesIntensiveOutcome.DeadlineExceeded, original);
        Assert.ThrowsExactly<FormatException>(() => TimeSeriesIntensiveFailure.From(observed));
        var captured = TimeSeriesIntensiveFailure.Capture(observed);
        await Assert.That(captured.Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.DeadlineExceeded);
        await Assert.That(captured.Failure).IsEqualTo(new TimeSeriesIntensiveFailure(
            TimeSeriesIntensiveFailureOrigin.Unexpected, null, null, null));
        await Assert.That(TimeSeriesIntensiveFailure.Capture(original).Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.UnexpectedFailure);
    }

    private static Exception[] NativeErrors() => [
        new KeyLoadException(ErrorCode.UnknownWriteOutcome, "private fixture detail", 503),
        new PostgresException("private fixture detail", "ERROR", "ERROR", "23505"),
        new HttpRequestException("private fixture detail", null, System.Net.HttpStatusCode.BadGateway),
        new OperationCanceledException(), new InvalidOperationException("private fixture detail")];

    private static Exception FatalInput(Type type)
    {
        if (type != typeof(OutOfMemoryException) && type != typeof(StackOverflowException) && type != typeof(AccessViolationException))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        // Real BCL classification inputs only; no catastrophic exception is raised.
        return (Exception)(Activator.CreateInstance(type) ?? throw new InvalidOperationException("Missing BCL exception constructor."));
    }
}

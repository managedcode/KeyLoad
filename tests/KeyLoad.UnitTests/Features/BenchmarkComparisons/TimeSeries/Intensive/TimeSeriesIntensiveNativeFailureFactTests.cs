using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using Npgsql;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveNativeFailureFactTests
{
    private const int ReturnedStatus = 418;
    private const long ObservedSequence = 918;
    private const string Secret = "native-private-detail";
    private const string Severity = "ERROR";

    /// <summary>AC-TSI-004/008: retain actual returned status, even when unlike the code's default.</summary>
    [Test]
    public async Task ActualProblemRetainsReturnedCodeAndStatusWithoutRecomputation()
    {
        var error = new KeyLoadTimeSeriesIntensiveProblemException(ErrorCode.Conflict, ReturnedStatus);
        var captured = TimeSeriesIntensiveFailure.Capture(error);
        await Assert.That(captured.Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.TargetFailure);
        await Assert.That(captured.Failure).IsEqualTo(new TimeSeriesIntensiveFailure(
            TimeSeriesIntensiveFailureOrigin.KeyLoad, ErrorCode.Conflict, ReturnedStatus, null));
    }

    [Test]
    public async Task StandardProblemConstructionNeverInventsNativeFacts()
    {
        KeyLoadTimeSeriesIntensiveProblemException[] errors =
        [new(), new(Secret), new(Secret, new InvalidOperationException(Secret))];
        foreach (var error in errors)
        {
            var captured = TimeSeriesIntensiveFailure.Capture(error);
            await Assert.That(captured.Failure).IsEqualTo(new TimeSeriesIntensiveFailure(
                TimeSeriesIntensiveFailureOrigin.KeyLoad, null, null, null));
            await Assert.That(captured.Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.TargetFailure);
        }
    }

    /// <summary>AC-TSI-004: malformed authority can retain a labelled Revision but certifies no sample count.</summary>
    [Test]
    public async Task ReplyValidationRetainsObservedSequenceWithNoCountOrSuccessDigest()
    {
        var error = new KeyLoadTimeSeriesIntensiveReplyException(ObservedSequence);
        var attempt = TimeSeriesIntensiveAttemptFailures.Capture(0, 17, 1, 3,
            TimeSeriesIntensiveOutcome.Succeeded, error);
        await Assert.That(attempt.Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.ValidationFailure);
        await Assert.That(attempt.Failure.Origin).IsEqualTo(TimeSeriesIntensiveFailureOrigin.Oracle);
        await Assert.That(attempt.ReceiptSequence).IsEqualTo(ObservedSequence);
        await Assert.That(attempt.ResultCount).IsNull();
        await Assert.That(attempt.Digest).IsEqualTo(default(TimeSeriesIntensiveHash));
        await Assert.That(attempt.ValidationTicks).IsEqualTo(0L);
    }

    [Test]
    public async Task ObservedDeadlineKeepsOnlyActualMatchingReplySequence()
    {
        var error = TimeSeriesIntensiveObservedFailureException.Observe(
            TimeSeriesIntensiveOutcome.DeadlineExceeded, new KeyLoadTimeSeriesIntensiveReplyException(ObservedSequence));
        var attempt = TimeSeriesIntensiveAttemptFailures.Capture(0, 17, 1, 3,
            TimeSeriesIntensiveOutcome.DeadlineExceeded, error);
        await Assert.That(attempt.Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.DeadlineExceeded);
        await Assert.That(attempt.ReceiptSequence).IsEqualTo(ObservedSequence);
        await Assert.That(attempt.ResultCount).IsNull();
        await Assert.That(attempt.Digest).IsEqualTo(default(TimeSeriesIntensiveHash));
        await Assert.That(attempt.Failure.Origin).IsEqualTo(TimeSeriesIntensiveFailureOrigin.Oracle);
    }

    /// <summary>Actual exception-input proof; native blocked Npgsql cancellation remains a separate gate.</summary>
    [Test]
    public async Task ActualInnerPostgresCancellationRetainsSqlStateAndDeadlineOutcome()
    {
        var native = new PostgresException(Secret, Severity, Severity, PostgresErrorCodes.QueryCanceled);
        var cancelled = new OperationCanceledException(Secret, native);
        var observed = TimeSeriesIntensiveObservedFailureException.Observe(TimeSeriesIntensiveOutcome.DeadlineExceeded, cancelled);
        var captured = TimeSeriesIntensiveFailure.Capture(observed);
        await Assert.That(captured.Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.DeadlineExceeded);
        await Assert.That(captured.Failure).IsEqualTo(new TimeSeriesIntensiveFailure(
            TimeSeriesIntensiveFailureOrigin.PostgreSQL, null, null,
            TimeSeriesIntensiveFailure.PackSqlState(PostgresErrorCodes.QueryCanceled)));
    }

    [Test]
    public async Task BareCancellationNeverAcquiresAQueryCanceledSqlState()
    {
        var captured = TimeSeriesIntensiveFailure.Capture(new OperationCanceledException(Secret));
        await Assert.That(captured.Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.Cancelled);
        await Assert.That(captured.Failure).IsEqualTo(new TimeSeriesIntensiveFailure(
            TimeSeriesIntensiveFailureOrigin.Client, null, null, null));
    }
}

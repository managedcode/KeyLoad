using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using Npgsql;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveCompletionTests
{
    private const string PrivateDetail = "private-provider-detail";
    private const string Severity = "ERROR";
    private const long ActualSequence = 918;
    private static readonly Guid ActualCommand = new("ad10433c-4c18-4f9b-972a-d306bf0aa783");

    /// <summary>AC-TSI-004: a failed postcommit cleanup keeps actual ACK facts without successful data.</summary>
    [Test]
    public async Task PostcommitFailureRetainsActualAckWithoutCountOrDigest()
    {
        var ack = TimeSeriesIntensiveAcknowledgement.FromReceipt(new(ActualCommand, ActualSequence));
        var cleanup = Native(PostgresErrorCodes.ConnectionFailure);
        var error = TimeSeriesIntensiveTargetCompletionException.Join(null, cleanup, ack)!;
        var attempt = Capture(error);
        await Assert.That(attempt.Outcome).IsEqualTo(TimeSeriesIntensiveOutcome.TargetFailure);
        await Assert.That(attempt.Acknowledgement).IsEqualTo((TimeSeriesIntensiveAcknowledgement?)ack);
        await Assert.That(attempt.ReceiptSequence).IsEqualTo(ActualSequence);
        await Assert.That(attempt.ResultCount).IsNull();
        await Assert.That(attempt.Digest).IsEqualTo(default(TimeSeriesIntensiveHash));
        await Assert.That(attempt.CleanupFailure.SqlState).IsEqualTo((ulong?)TimeSeriesIntensiveFailure.PackSqlState(cleanup.SqlState));
    }

    [Test]
    public async Task UnknownCommitPreservesPrimaryNativeFactAndSeparateCleanup()
    {
        var original = Native(PostgresErrorCodes.SerializationFailure);
        var cleanup = Native(PostgresErrorCodes.ConnectionFailure);
        var error = TimeSeriesIntensiveTargetCompletionException.Join(original, cleanup, null)!;
        var attempt = Capture(error);
        await Assert.That(error.InnerException).IsSameReferenceAs(original);
        await Assert.That(attempt.Acknowledgement).IsNull();
        await Assert.That(attempt.ReceiptSequence).IsEqualTo(0L);
        await Assert.That(attempt.Failure.SqlState).IsEqualTo((ulong?)TimeSeriesIntensiveFailure.PackSqlState(original.SqlState));
        await Assert.That(attempt.CleanupFailure.SqlState).IsEqualTo((ulong?)TimeSeriesIntensiveFailure.PackSqlState(cleanup.SqlState));
    }

    [Test]
    [Arguments(TimeSeriesIntensiveOutcome.DeadlineExceeded)]
    [Arguments(TimeSeriesIntensiveOutcome.Cancelled)]
    public async Task InterruptedOutcomeRetainsIndependentNativeCleanupAndAckFacts(TimeSeriesIntensiveOutcome outcome)
    {
        var ack = TimeSeriesIntensiveAcknowledgement.FromReceipt(new(ActualCommand, ActualSequence));
        var error = TimeSeriesIntensiveTargetCompletionException.Join(null, Native(PostgresErrorCodes.ConnectionFailure), ack)!;
        var observed = TimeSeriesIntensiveObservedFailureException.Observe(outcome, error);
        var attempt = Capture(observed);
        await Assert.That(attempt.Outcome).IsEqualTo(outcome);
        await Assert.That(attempt.Acknowledgement).IsEqualTo((TimeSeriesIntensiveAcknowledgement?)ack);
        await Assert.That(attempt.CleanupFailure.Origin).IsEqualTo(TimeSeriesIntensiveFailureOrigin.PostgreSQL);
        await Assert.That(attempt.ResultCount).IsNull();
    }

    [Test]
    public async Task FatalPrecedenceReturnsOriginalWithoutWrapping()
    {
        var primary = RuntimeOversizeFailure();
        var cleanup = RuntimeOversizeFailure();
        await Assert.That(TimeSeriesIntensiveTargetCompletionException.Join(primary, cleanup, null)).IsSameReferenceAs(primary);
        await Assert.That(TimeSeriesIntensiveTargetCompletionException.Join(Native(PostgresErrorCodes.QueryCanceled), cleanup, null)).IsSameReferenceAs(cleanup);
    }

    [Test]
    public async Task SuccessfulCompletionAndSingleFailureDoNotInventFacts()
    {
        var original = Native(PostgresErrorCodes.QueryCanceled);
        await Assert.That(TimeSeriesIntensiveTargetCompletionException.Join(null, null, null)).IsNull();
        await Assert.That(TimeSeriesIntensiveTargetCompletionException.Join(original, null, null)).IsSameReferenceAs(original);
        await Assert.That(Capture(original).Acknowledgement).IsNull();
        await Assert.That(Capture(original).CleanupFailure).IsEqualTo(default(TimeSeriesIntensiveFailure));
    }

    [Test]
    public async Task MalformedReceiptsCannotProduceAcknowledgement()
    {
        await Assert.That(() => TimeSeriesIntensiveAcknowledgement.FromReceipt(new(Guid.Empty, ActualSequence))).Throws<ArgumentException>();
        await Assert.That(() => TimeSeriesIntensiveAcknowledgement.FromReceipt(new(ActualCommand, 0))).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => TimeSeriesIntensiveAcknowledgement.FromReceipt(new(ActualCommand, -1))).Throws<ArgumentOutOfRangeException>();
    }

    private static PostgresException Native(string code) => new(PrivateDetail, Severity, Severity, code);

    private static OutOfMemoryException RuntimeOversizeFailure()
    {
        try
        {
            // CLR rejects this impossible element count before allocating an array payload.
            _ = Array.CreateInstance(typeof(byte), int.MaxValue, int.MaxValue);
        }
        catch (OutOfMemoryException error)
        {
            return error;
        }
        throw new InvalidOperationException("The runtime did not reject the impossible array dimensions.");
    }

    private static TimeSeriesIntensiveAttempt Capture(Exception error) =>
        TimeSeriesIntensiveAttemptFailures.Capture(0, 1, 0, 3, TimeSeriesIntensiveOutcome.Succeeded, error);
}

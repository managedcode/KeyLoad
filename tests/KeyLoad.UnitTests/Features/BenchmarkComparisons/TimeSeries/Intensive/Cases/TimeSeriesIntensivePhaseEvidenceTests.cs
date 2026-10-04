using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensivePhaseEvidenceTests
{
    [Test]
    public async Task AcTsi004CompletedMeasuredFailureRetainsOnlyObservedPhaseFacts()
    {
        var warmup = new TimeSeriesIntensivePhaseResult(true, true, 500, 16, 16, 16, null);
        var measurement = new TimeSeriesIntensiveMeasurement(10000, 10000, 20, 500, 7, 1, 2, 3);
        var measured = new TimeSeriesIntensivePhaseResult(true, true, 20000, 16, 16, 16, measurement);
        var result = new TimeSeriesIntensiveRepetitionResult(2, warmup, measured, true, null);
        var failure = new TimeSeriesIntensiveRunFailure(TimeSeriesIntensiveRunStage.FinalVerification, 2,
            TimeSeriesIntensiveOutcome.Cancelled, new(TimeSeriesIntensiveFailureOrigin.Client, null, null, null));
        var observed = result.WithVerificationFailure(failure);
        await Assert.That(observed.Warmup).IsEqualTo(warmup);
        await Assert.That(observed.Measured).IsEqualTo(measured);
        await Assert.That(observed.Measured.Measurement).IsSameReferenceAs(measurement);
        await Assert.That(observed.FinalVerified).IsFalse();
        await Assert.That(observed.Failure).IsEqualTo(failure);
        await Assert.That(observed.Succeeded).IsFalse();
    }

    [Test]
    public async Task AcTsi004CompletedWarmupFailureDoesNotInventAMeasuredPhase()
    {
        var warmup = new TimeSeriesIntensivePhaseResult(false, false, 900, 16, 8, 7, null);
        var result = new TimeSeriesIntensiveRepetitionResult(1, warmup, default, false, null);
        var failure = new TimeSeriesIntensiveRunFailure(TimeSeriesIntensiveRunStage.WarmupVerification, 1,
            TimeSeriesIntensiveOutcome.DeadlineExceeded, new(TimeSeriesIntensiveFailureOrigin.Client, null, null, null));
        var observed = result.WithVerificationFailure(failure);
        await Assert.That(observed.Warmup).IsEqualTo(warmup);
        await Assert.That(observed.Measured).IsEqualTo(default(TimeSeriesIntensivePhaseResult));
        await Assert.That(observed.FinalVerified).IsFalse();
        await Assert.That(observed.Failure).IsEqualTo(failure);
        await Assert.That(observed.Succeeded).IsFalse();
    }

    [Test]
    public async Task AcTsi004MeasuredEntryFailureRetainsCompletedWarmupAndNoUnobservedPhase()
    {
        var warmup = new TimeSeriesIntensivePhaseResult(true, true, 900, 16, 8, 7, null);
        var failure = new TimeSeriesIntensiveRunFailure(TimeSeriesIntensiveRunStage.Measured, 1,
            TimeSeriesIntensiveOutcome.Cancelled, new(TimeSeriesIntensiveFailureOrigin.Client, null, null, null));
        var result = new TimeSeriesIntensiveRepetitionResult(1, warmup, default, false, null);
        var observed = result.WithVerificationFailure(failure);
        await Assert.That(observed.Warmup).IsEqualTo(warmup);
        await Assert.That(observed.Warmup.Succeeded).IsTrue();
        await Assert.That(observed.Measured).IsEqualTo(default(TimeSeriesIntensivePhaseResult));
        await Assert.That(observed.Measured.Measurement).IsNull();
        await Assert.That(observed.FinalVerified).IsFalse();
        await Assert.That(observed.Failure).IsEqualTo(failure);
        await Assert.That(observed.Succeeded).IsFalse();
    }

    [Test]
    public async Task AcTsi004PrimaryMeasuredFailureSurvivesAReadbackFailure()
    {
        var primary = new TimeSeriesIntensiveRunFailure(TimeSeriesIntensiveRunStage.Measured, 3,
            TimeSeriesIntensiveOutcome.TargetFailure, new(TimeSeriesIntensiveFailureOrigin.KeyLoad, ErrorCode.OwnershipLost, 503, null));
        var later = new TimeSeriesIntensiveRunFailure(TimeSeriesIntensiveRunStage.FinalVerification, 3,
            TimeSeriesIntensiveOutcome.Cancelled, new(TimeSeriesIntensiveFailureOrigin.Client, null, null, null));
        var measured = new TimeSeriesIntensivePhaseResult(true, false, 12000, 16, 16, 16, null);
        var result = new TimeSeriesIntensiveRepetitionResult(3, default, measured, false, primary);
        var observed = result.WithVerificationFailure(later);
        await Assert.That(observed.Failure).IsEqualTo(primary);
        await Assert.That(observed.Measured).IsEqualTo(measured);
        await Assert.That(observed.FinalVerified).IsFalse();
    }
}

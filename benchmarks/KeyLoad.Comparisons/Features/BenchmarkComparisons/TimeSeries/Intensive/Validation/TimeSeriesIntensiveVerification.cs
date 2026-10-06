using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveVerification
{
    internal static async Task SeedAsync(ITimeSeriesIntensiveTarget target, IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        foreach (var readback in TimeSeriesIntensivePlans.SeedReadbacks())
        {
            var expected = TimeSeriesIntensiveOracle.SeedReadback(readback);
            var actual = await TimeSeriesIntensiveVerificationReader.ReadAsync(target, readback.SeriesId,
                readback.From, readback.Until, executionOptions, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
            _ = TimeSeriesIntensiveResultDigest.ValidatedRaw(expected, actual);
        }

        var expectedAggregate = TimeSeriesIntensiveStatistics.Fold(TimeSeriesIntensiveCorpus.SeedOrdered);
        var actualAggregate = await TimeSeriesIntensiveVerificationReader.WholeAsync(target, TimeSeriesIntensiveProfile.SeedSeries, executionOptions, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        TimeSeriesIntensiveOracle.ValidateFullCount(TimeSeriesIntensiveProfile.SeedSeries, actualAggregate);
        TimeSeriesIntensiveOracle.ValidateAggregate(expectedAggregate, actualAggregate);
    }

    internal static async Task EmptyAsync(ITimeSeriesIntensiveTarget target, string seriesId, IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        const int NoObservedItems = 0;

        var raw = await TimeSeriesIntensiveVerificationReader.ReadAsync(target, seriesId,
            DateTimeOffset.MinValue, DateTimeOffset.MaxValue, executionOptions, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        _ = TimeSeriesIntensiveResultDigest.ValidatedRaw([], raw);
        var latest = await TimeSeriesIntensiveVerificationReader.LatestAsync(target, seriesId, executionOptions, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        _ = TimeSeriesIntensiveResultDigest.ValidatedLatest(null, latest);
        var aggregate = await TimeSeriesIntensiveVerificationReader.WholeAsync(target, seriesId, executionOptions, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        TimeSeriesIntensiveOracle.ValidateAggregate(new(NoObservedItems, NoObservedItems, null, null, null), aggregate);
    }

    internal static async Task<TimeSeriesIntensiveRunFailure?> AppendAsync(ITimeSeriesIntensiveTarget target, TimeSeriesIntensiveExpectations expected, TimeSeriesIntensivePreparedPhase phase, TimeSeriesIntensiveAttemptLedger ledger, IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        TimeSeriesIntensiveRunFailure? failure = null;
        var view = new TimeSeriesIntensiveReceiptView(phase.Commands, ledger.Attempts);
        foreach (var readback in TimeSeriesIntensivePlans.AppendReadbacks(phase.Repetition, phase.Warmup))
        {
            try
            {
                var actual = await TimeSeriesIntensiveVerificationReader.ReadAsync(target, readback.SeriesId,
                    readback.From, readback.Until, executionOptions, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
                var rows = TimeSeriesIntensiveAppendOracle.Readback(readback, view);
                _ = TimeSeriesIntensiveResultDigest.ValidatedRaw(rows, actual);
            }
            catch (Exception error) when (TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
            {
                failure ??= Failed(phase, error);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        var wholeFailure = await WholeAppendAsync(target, expected, phase, executionOptions, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        return failure ?? wholeFailure;
    }

    private static async Task<TimeSeriesIntensiveRunFailure?> WholeAppendAsync(ITimeSeriesIntensiveTarget target, TimeSeriesIntensiveExpectations expected, TimeSeriesIntensivePreparedPhase phase, IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        const int NoObservedItems = 0;

        try
        {
            var aggregate = TimeSeriesIntensiveStatistics.Fold(expected.AppendSamples.Take(phase.Count)
                .Select(sample => new SampleRecord(phase.SeriesId, sample, NoObservedItems, TimeSeriesIntensiveProfile.Tags)));
            var actual = await TimeSeriesIntensiveVerificationReader.WholeAsync(target, phase.SeriesId, executionOptions, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
            TimeSeriesIntensiveOracle.ValidateFullCount(phase.SeriesId, actual);
            TimeSeriesIntensiveOracle.ValidateAggregate(aggregate, actual);
            phase.Appends!.ValidateComplete();
            return null;
        }
        catch (Exception error) when (TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
        {
            return Failed(phase, error);
        }
    }

    private static TimeSeriesIntensiveRunFailure Failed(TimeSeriesIntensivePreparedPhase phase, Exception error)
    {
        var captured = TimeSeriesIntensiveFailure.Capture(error);
        return new(phase.Warmup ? TimeSeriesIntensiveRunStage.WarmupVerification : TimeSeriesIntensiveRunStage.FinalVerification,
            phase.Repetition, captured.Outcome, captured.Failure);
    }
}

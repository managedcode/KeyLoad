namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveVerification
{
    internal static async Task SeedAsync(ITimeSeriesIntensiveTarget target, CancellationToken cancellationToken)
    {
        foreach (var readback in TimeSeriesIntensivePlans.SeedReadbacks())
        {
            var expected = TimeSeriesIntensiveOracle.SeedReadback(readback);
            var actual = await TimeSeriesIntensiveVerificationReader.ReadAsync(target, readback.SeriesId,
                readback.From, readback.Until, cancellationToken).ConfigureAwait(false);
            _ = TimeSeriesIntensiveResultDigest.ValidatedRaw(expected, actual);
        }

        var expectedAggregate = TimeSeriesIntensiveStatistics.Fold(TimeSeriesIntensiveCorpus.SeedOrdered);
        var actualAggregate = await TimeSeriesIntensiveVerificationReader.WholeAsync(target, TimeSeriesIntensiveProfile.SeedSeries, cancellationToken).ConfigureAwait(false);
        TimeSeriesIntensiveOracle.ValidateFullCount(TimeSeriesIntensiveProfile.SeedSeries, actualAggregate);
        TimeSeriesIntensiveOracle.ValidateAggregate(expectedAggregate, actualAggregate);
    }

    internal static async Task EmptyAsync(ITimeSeriesIntensiveTarget target, string seriesId, CancellationToken cancellationToken)
    {
        var raw = await TimeSeriesIntensiveVerificationReader.ReadAsync(target, seriesId,
            DateTimeOffset.MinValue, DateTimeOffset.MaxValue, cancellationToken).ConfigureAwait(false);
        _ = TimeSeriesIntensiveResultDigest.ValidatedRaw([], raw);
        var latest = await TimeSeriesIntensiveVerificationReader.LatestAsync(target, seriesId, cancellationToken).ConfigureAwait(false);
        _ = TimeSeriesIntensiveResultDigest.ValidatedLatest(null, latest);
        var aggregate = await TimeSeriesIntensiveVerificationReader.WholeAsync(target, seriesId, cancellationToken).ConfigureAwait(false);
        TimeSeriesIntensiveOracle.ValidateAggregate(new(0, 0, null, null, null), aggregate);
    }

    internal static async Task<TimeSeriesIntensiveRunFailure?> AppendAsync(ITimeSeriesIntensiveTarget target,
        TimeSeriesIntensiveExpectations expected, TimeSeriesIntensivePreparedPhase phase,
        TimeSeriesIntensiveAttemptLedger ledger, CancellationToken cancellationToken)
    {
        TimeSeriesIntensiveRunFailure? failure = null;
        var view = new TimeSeriesIntensiveReceiptView(phase.Commands, ledger.Attempts);
        foreach (var readback in TimeSeriesIntensivePlans.AppendReadbacks(phase.Repetition, phase.Warmup))
        {
            try
            {
                var actual = await TimeSeriesIntensiveVerificationReader.ReadAsync(target, readback.SeriesId,
                    readback.From, readback.Until, cancellationToken).ConfigureAwait(false);
                var rows = TimeSeriesIntensiveAppendOracle.Readback(readback, view);
                _ = TimeSeriesIntensiveResultDigest.ValidatedRaw(rows, actual);
            }
            catch (Exception error) when (TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
            {
                failure ??= Failed(phase, error);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        var wholeFailure = await WholeAppendAsync(target, expected, phase, cancellationToken).ConfigureAwait(false);
        return failure ?? wholeFailure;
    }

    private static async Task<TimeSeriesIntensiveRunFailure?> WholeAppendAsync(ITimeSeriesIntensiveTarget target,
        TimeSeriesIntensiveExpectations expected, TimeSeriesIntensivePreparedPhase phase, CancellationToken cancellationToken)
    {
        try
        {
            var aggregate = TimeSeriesIntensiveStatistics.Fold(expected.AppendSamples.Take(phase.Count)
                .Select(sample => new SampleRecord(phase.SeriesId, sample, 0, TimeSeriesIntensiveProfile.Tags)));
            var actual = await TimeSeriesIntensiveVerificationReader.WholeAsync(target, phase.SeriesId, cancellationToken).ConfigureAwait(false);
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

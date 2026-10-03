namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveRepetitionExecutor(ITimeSeriesIntensiveTarget target,
    TimeSeriesIntensiveExpectations expected, TimeSeriesIntensiveScenario scenario, string runId,
    int repetition, TimeSeriesIntensiveAttempt[] storage, TimeSeriesIntensiveAttempt[] warmupStorage,
    CancellationToken cellCancellation)
{
    internal TimeSeriesIntensiveRunStage Stage { get; private set; } = TimeSeriesIntensiveRunStage.Warmup;

    internal async Task<TimeSeriesIntensiveRepetitionResult> RunAsync()
    {
        var warmup = await WarmupAsync().ConfigureAwait(false);
        if (!warmup.Result.Succeeded || warmup.Failure.HasValue)
        {
            return new(repetition, warmup.Result, default, false, warmup.Failure);
        }

        try
        {
            return await MeasureAsync(warmup.Result).ConfigureAwait(false);
        }
        catch (Exception error) when (TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
        {
            return new(repetition, warmup.Result, default, false, VerificationFailure(Stage, error));
        }
    }

    private async Task<TimeSeriesIntensiveRepetitionResult> MeasureAsync(TimeSeriesIntensivePhaseResult warmup)
    {
        Stage = TimeSeriesIntensiveRunStage.Measured;
        cellCancellation.ThrowIfCancellationRequested();
        var phase = TimeSeriesIntensivePreparedPhase.Create(scenario, runId, repetition, false);
        if (scenario == TimeSeriesIntensiveScenario.Append)
        {
            await TimeSeriesIntensiveVerification.EmptyAsync(target, phase.SeriesId, cellCancellation).ConfigureAwait(false);
        }

        var ledger = new TimeSeriesIntensiveAttemptLedger(storage, repetition * TimeSeriesIntensiveProfile.OperationCount, phase.Count, repetition);
        var measured = await ExecutePhaseAsync(phase, ledger).ConfigureAwait(false);
        var failure = FirstFailure(ledger, TimeSeriesIntensiveRunStage.Measured);
        var result = new TimeSeriesIntensiveRepetitionResult(repetition, warmup, measured, measured.Succeeded, failure);
        Stage = TimeSeriesIntensiveRunStage.FinalVerification;
        try
        {
            var final = scenario == TimeSeriesIntensiveScenario.Append
                ? await TimeSeriesIntensiveVerification.AppendAsync(target, expected, phase, ledger, cellCancellation).ConfigureAwait(false) : null;
            return final.HasValue ? result.WithVerificationFailure(final.Value) : result;
        }
        catch (Exception error) when (TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
        {
            return result.WithVerificationFailure(VerificationFailure(TimeSeriesIntensiveRunStage.FinalVerification, error));
        }
    }

    private async Task<(TimeSeriesIntensivePhaseResult Result, TimeSeriesIntensiveRunFailure? Failure)> WarmupAsync()
    {
        Stage = TimeSeriesIntensiveRunStage.Warmup;
        var phase = TimeSeriesIntensivePreparedPhase.Create(scenario, runId, repetition, true);
        if (scenario == TimeSeriesIntensiveScenario.Append)
        {
            await TimeSeriesIntensiveVerification.EmptyAsync(target, phase.SeriesId, cellCancellation).ConfigureAwait(false);
        }

        var offset = repetition * TimeSeriesIntensiveProfile.WarmupCount;
        var ledger = new TimeSeriesIntensiveAttemptLedger(warmupStorage, offset, phase.Count, repetition);
        var result = await ExecutePhaseAsync(phase, ledger).ConfigureAwait(false);
        var failure = FirstFailure(ledger, TimeSeriesIntensiveRunStage.Warmup);
        Stage = TimeSeriesIntensiveRunStage.WarmupVerification;
        try
        {
            var verification = scenario == TimeSeriesIntensiveScenario.Append
                ? await TimeSeriesIntensiveVerification.AppendAsync(target, expected, phase, ledger, cellCancellation).ConfigureAwait(false) : null;
            return (result, failure ?? verification);
        }
        catch (Exception error) when (TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
        {
            return (result, failure ?? VerificationFailure(TimeSeriesIntensiveRunStage.WarmupVerification, error));
        }
    }

    private async Task<TimeSeriesIntensivePhaseResult> ExecutePhaseAsync(TimeSeriesIntensivePreparedPhase phase, TimeSeriesIntensiveAttemptLedger ledger)
    {
        await using var executor = new TimeSeriesIntensivePhaseExecutor(target, expected, phase, ledger, cellCancellation);
        return await executor.RunAsync().ConfigureAwait(false);
    }

    private TimeSeriesIntensiveRunFailure? FirstFailure(TimeSeriesIntensiveAttemptLedger ledger, TimeSeriesIntensiveRunStage stage)
    {
        foreach (var attempt in ledger.Attempts.Span)
        {
            if (attempt.Outcome != TimeSeriesIntensiveOutcome.Succeeded)
            {
                return new(stage, repetition, attempt.Outcome, attempt.Failure);
            }
        }

        return null;
    }

    private TimeSeriesIntensiveRunFailure VerificationFailure(TimeSeriesIntensiveRunStage stage, Exception error)
    {
        var captured = TimeSeriesIntensiveFailure.Capture(error);
        return new(stage, repetition, captured.Outcome, captured.Failure);
    }
}

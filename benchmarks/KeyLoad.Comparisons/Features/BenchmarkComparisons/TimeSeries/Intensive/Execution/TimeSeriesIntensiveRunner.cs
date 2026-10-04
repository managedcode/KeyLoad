namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveRunner
{
    internal static async Task<TimeSeriesIntensiveRunResult> RunAsync(ITimeSeriesIntensiveTarget target,
        TimeSeriesIntensiveScenario scenario, string runId, CancellationToken cellCancellation)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (!Enum.IsDefined(scenario))
        {
            throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var state = new TimeSeriesIntensiveRunState(scenario);
        try
        {
            await ExecuteAsync(target, scenario, runId, state, cellCancellation).ConfigureAwait(false);
            return state.Finish();
        }
        catch (Exception error) when (TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
        {
            return state.Finish(error);
        }
    }

    private static async Task ExecuteAsync(ITimeSeriesIntensiveTarget target, TimeSeriesIntensiveScenario scenario,
        string runId, TimeSeriesIntensiveRunState state, CancellationToken cellCancellation)
    {
        cellCancellation.ThrowIfCancellationRequested();
        var expected = TimeSeriesIntensiveExpectations.Create();
        await PrepareAsync(target, state, cellCancellation).ConfigureAwait(false);
        for (; state.Repetition < TimeSeriesIntensiveProfile.RepetitionCount; state.Repetition++)
        {
            cellCancellation.ThrowIfCancellationRequested();
            state.Executor = new(target, expected, scenario, runId, state.Repetition, state.Storage,
                state.WarmupStorage, cellCancellation);
            var result = await state.Executor.RunAsync().ConfigureAwait(false);
            state.Repetitions.Add(result);
            if (!result.Warmup.Succeeded || result.Measured.WorkersStarted == 0)
            {
                break;
            }
        }

        state.Executor = null;
        state.Stage = TimeSeriesIntensiveRunStage.FinalVerification;
        await TimeSeriesIntensiveVerification.SeedAsync(target, cellCancellation).ConfigureAwait(false);
    }

    private static async Task PrepareAsync(ITimeSeriesIntensiveTarget target, TimeSeriesIntensiveRunState state,
        CancellationToken cellCancellation)
    {
        state.Stage = TimeSeriesIntensiveRunStage.Initialization;
        await TimeSeriesIntensiveSetupExecutor.InitializeAsync(target, cellCancellation).ConfigureAwait(false);
        state.Stage = TimeSeriesIntensiveRunStage.Seed;
        await TimeSeriesIntensiveSetupExecutor.SeedAsync(target, cellCancellation).ConfigureAwait(false);
        state.Stage = TimeSeriesIntensiveRunStage.SeedVerification;
        await TimeSeriesIntensiveVerification.SeedAsync(target, cellCancellation).ConfigureAwait(false);
        state.SeedVerified = true;
    }
}

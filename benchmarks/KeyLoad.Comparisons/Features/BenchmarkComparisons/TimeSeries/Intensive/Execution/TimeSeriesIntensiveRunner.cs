using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveRunner
{
    internal static async Task<TimeSeriesIntensiveRunResult> RunAsync(ITimeSeriesIntensiveTarget target, TimeSeriesIntensiveScenario scenario, string runId, IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken cellCancellation)
    {
        NativeComparisonExecutionOptions.Require(executionOptions);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (!Enum.IsDefined(scenario))
        {
            throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var state = new TimeSeriesIntensiveRunState(scenario, provider: timeProvider);
        try
        {
            await ExecuteAsync(target, scenario, runId, state, executionOptions, cellCancellation: cellCancellation, timeProvider: timeProvider).ConfigureAwait(false);
            return state.Finish();
        }
        catch (Exception error) when (TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
        {
            return state.Finish(error);
        }
    }

    private static async Task ExecuteAsync(ITimeSeriesIntensiveTarget target, TimeSeriesIntensiveScenario scenario, string runId, TimeSeriesIntensiveRunState state, IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken cellCancellation)
    {
        const int NoObservedItems = 0;

        cellCancellation.ThrowIfCancellationRequested();
        var expected = TimeSeriesIntensiveExpectations.Create();
        await PrepareAsync(target, state, executionOptions, cellCancellation: cellCancellation, timeProvider: timeProvider).ConfigureAwait(false);
        for (; state.Repetition < TimeSeriesIntensiveProfile.RepetitionCount; state.Repetition++)
        {
            cellCancellation.ThrowIfCancellationRequested();
            state.Executor = new(target, expected, scenario, runId, state.Repetition, state.Storage,
                state.WarmupStorage, executionOptions, cellCancellation, timeProvider);
            var result = await state.Executor.RunAsync().ConfigureAwait(false);
            state.Repetitions.Add(result);
            if (!result.Warmup.Succeeded || result.Measured.WorkersStarted == NoObservedItems)
            {
                break;
            }
        }

        state.Executor = null;
        state.Stage = TimeSeriesIntensiveRunStage.FinalVerification;
        await TimeSeriesIntensiveVerification.SeedAsync(target, executionOptions, cancellationToken: cellCancellation, timeProvider: timeProvider).ConfigureAwait(false);
    }

    private static async Task PrepareAsync(ITimeSeriesIntensiveTarget target, TimeSeriesIntensiveRunState state, IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken cellCancellation)
    {
        state.Stage = TimeSeriesIntensiveRunStage.Initialization;
        await TimeSeriesIntensiveSetupExecutor.InitializeAsync(target, executionOptions, cancellationToken: cellCancellation, timeProvider: timeProvider).ConfigureAwait(false);
        state.Stage = TimeSeriesIntensiveRunStage.Seed;
        await TimeSeriesIntensiveSetupExecutor.SeedAsync(target, executionOptions, cancellationToken: cellCancellation, timeProvider: timeProvider).ConfigureAwait(false);
        state.Stage = TimeSeriesIntensiveRunStage.SeedVerification;
        await TimeSeriesIntensiveVerification.SeedAsync(target, executionOptions, cancellationToken: cellCancellation, timeProvider: timeProvider).ConfigureAwait(false);
        state.SeedVerified = true;
    }
}

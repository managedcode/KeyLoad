using System.Diagnostics;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveAttemptExecutor
{
    internal static async Task<TimeSeriesIntensiveAttempt?> ExecuteAsync(ITimeSeriesIntensiveTarget target,
        TimeSeriesIntensiveExpectations expected, TimeSeriesIntensivePreparedPhase phase, int index, int worker,
        TimeSeriesIntensiveConcurrency concurrency, CancellationToken cellCancellation)
    {
        using var call = new TimeSeriesIntensiveCallScope(cellCancellation);
        TimeSeriesIntensiveResponse? response;
        try
        {
            response = await TimeSeriesIntensiveResponseReader.ReadAsync(target, expected, phase, index, call, concurrency).ConfigureAwait(false);
        }
        catch (Exception error) when (TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
        {
            var captured = TimeSeriesIntensiveFailure.Capture(error);
            var outcome = call.Completion == TimeSeriesIntensiveOutcome.Succeeded ? captured.Outcome : call.Completion;
            return new(phase.Repetition, index, worker, call.LatencyTicks, 0, outcome, null, default, 0, captured.Failure);
        }

        if (response is not { } observed)
        {
            return null;
        }

        concurrency.EnterResponse();
        try
        {
            if (call.Completion != TimeSeriesIntensiveOutcome.Succeeded)
            {
                return new(phase.Repetition, index, worker, call.LatencyTicks, 0, call.Completion, observed.Count(phase.Scenario),
                    default, observed.Receipt?.Sequence ?? 0, new(TimeSeriesIntensiveFailureOrigin.Client, null, null, null));
            }

            return Validate(expected, phase, index, worker, call.LatencyTicks, observed);
        }
        finally
        {
            concurrency.ExitResponse();
        }
    }

    private static TimeSeriesIntensiveAttempt Validate(TimeSeriesIntensiveExpectations expected,
        TimeSeriesIntensivePreparedPhase phase, int index, int worker, long latency, TimeSeriesIntensiveResponse response)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var hash = TimeSeriesIntensiveResponseVerifier.Validate(expected, phase, index, response);
            return new(phase.Repetition, index, worker, latency, Stopwatch.GetTimestamp() - started,
                TimeSeriesIntensiveOutcome.Succeeded, response.Count(phase.Scenario), hash, response.Receipt?.Sequence ?? 0, default);
        }
        catch (Exception error) when (TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
        {
            var captured = TimeSeriesIntensiveFailure.Capture(error);
            return new(phase.Repetition, index, worker, latency, Stopwatch.GetTimestamp() - started,
                captured.Outcome, response.Count(phase.Scenario), default, response.Receipt?.Sequence ?? 0, captured.Failure);
        }
    }
}

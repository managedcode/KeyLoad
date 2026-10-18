using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveAttemptExecutor
{
    private const int NoObservedItems = 0;

    internal static async Task<TimeSeriesIntensiveAttempt?> ExecuteAsync(ITimeSeriesIntensiveTarget target, TimeSeriesIntensiveExpectations expected, TimeSeriesIntensivePreparedPhase phase, int index, int worker, TimeSeriesIntensiveConcurrency concurrency, IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken cellCancellation)
    {
        const int NoItems = 0;
        const int NoObservedItems = 0;

        using var call = new TimeSeriesIntensiveCallScope(executionOptions, cellCancellation, provider: timeProvider);
        TimeSeriesIntensiveResponse? response;
        try
        {
            response = await TimeSeriesIntensiveResponseReader.ReadAsync(target, expected, phase, index, call, concurrency).ConfigureAwait(false);
        }
        catch (Exception error) when (TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
        {
            return TimeSeriesIntensiveAttemptFailures.Capture(phase.Repetition, index, worker,
                call.LatencyTicks, call.Completion, error);
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
                return new(phase.Repetition, index, worker, call.LatencyTicks, NoItems, call.Completion, observed.Count(phase.Scenario),
                    default, observed.Receipt?.Sequence ?? NoObservedItems, new(TimeSeriesIntensiveFailureOrigin.Client, null, null, null))
                {
                    Acknowledgement = Acknowledgement(observed)
                };
            }

            return Validate(expected, phase, index, worker, call.LatencyTicks, observed, timeProvider: timeProvider);
        }
        finally
        {
            concurrency.ExitResponse();
        }
    }

    private static TimeSeriesIntensiveAttempt Validate(TimeSeriesIntensiveExpectations expected,
        TimeSeriesIntensivePreparedPhase phase, int index, int worker, long latency, TimeSeriesIntensiveResponse response, TimeProvider timeProvider)
    {
        const int NoItems = 0;

        var started = timeProvider.GetTimestamp();
        try
        {
            var hash = TimeSeriesIntensiveResponseVerifier.Validate(expected, phase, index, response);
            return new(phase.Repetition, index, worker, latency, timeProvider.GetTimestamp() - started,
                TimeSeriesIntensiveOutcome.Succeeded, response.Count(phase.Scenario), hash, response.Receipt?.Sequence ?? NoItems, default)
            {
                Acknowledgement = Acknowledgement(response)
            };
        }
        catch (Exception error) when (TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
        {
            var captured = TimeSeriesIntensiveFailure.Capture(error);
            return new(phase.Repetition, index, worker, latency, timeProvider.GetTimestamp() - started,
                captured.Outcome, response.Count(phase.Scenario), default, response.Receipt?.Sequence ?? NoItems, captured.Failure)
            {
                Acknowledgement = Acknowledgement(response)
            };
        }
    }

    private static TimeSeriesIntensiveAcknowledgement? Acknowledgement(TimeSeriesIntensiveResponse response) =>
        response.Receipt is { Sequence: > NoObservedItems } receipt && receipt.CommandId != Guid.Empty
            ? TimeSeriesIntensiveAcknowledgement.FromReceipt(receipt) : null;
}

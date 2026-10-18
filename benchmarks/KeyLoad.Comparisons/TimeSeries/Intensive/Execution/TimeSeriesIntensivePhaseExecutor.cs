using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensivePhaseExecutor(ITimeSeriesIntensiveTarget target,
    TimeSeriesIntensiveExpectations expected, TimeSeriesIntensivePreparedPhase phase,
    TimeSeriesIntensiveAttemptLedger ledger, IOptions<NativeComparisonExecutionOptions> executionOptions, CancellationToken cellCancellation, TimeProvider? provider = null) : IAsyncDisposable
{
    private readonly TimeProvider timeProvider = provider ?? TimeProvider.System;
    private readonly TimeSeriesIntensivePhaseWorkers workers = new(cellCancellation);
    private readonly TimeSeriesIntensiveConcurrency concurrency = new();

    internal async Task<TimeSeriesIntensivePhaseResult> RunAsync()
    {
        const int FirstElementIndex = 0;

        for (var index = FirstElementIndex; index < TimeSeriesIntensiveProfile.Concurrency; index++)
        {
            workers.Add(WorkAsync(index));
        }

        await workers.Ready.ConfigureAwait(false);
        var started = timeProvider.GetTimestamp();
        workers.Release();
        await workers.JoinAsync().ConfigureAwait(false);
        var wall = timeProvider.GetTimestamp() - started;
        var measurement = !phase.Warmup && ledger.Complete
            ? TimeSeriesIntensiveSummary.Calculate(ledger.Attempts.Span, wall, timeProvider.TimestampFrequency) : null;
        return new(ledger.Complete, ledger.Succeeded, wall, workers.WorkersStarted,
            concurrency.PeakRequests, concurrency.PeakResponses, measurement);
    }

    public ValueTask DisposeAsync() => workers.DisposeAsync();

    private async Task WorkAsync(int worker)
    {
        workers.SignalReady();
        await workers.Start.ConfigureAwait(false);
        try
        {
            foreach (var index in TimeSeriesIntensiveWorkerPartition.Indices(worker, phase.Count))
            {
                if (workers.IsCancellationRequested)
                {
                    break;
                }

                var attempt = await TimeSeriesIntensiveAttemptExecutor.ExecuteAsync(target, expected, phase,
                    index, worker, concurrency, executionOptions, cellCancellation: workers.Token, timeProvider: timeProvider).ConfigureAwait(false);
                if (!attempt.HasValue)
                {
                    break;
                }

                ledger.Publish(attempt.Value);
            }
        }
        catch (Exception primary) when (!TimeSeriesIntensiveExceptionBoundary.IsNonfatal(primary))
        {
            await workers.CancelAfterFatalAsync(primary).ConfigureAwait(false);
            throw;
        }
    }
}

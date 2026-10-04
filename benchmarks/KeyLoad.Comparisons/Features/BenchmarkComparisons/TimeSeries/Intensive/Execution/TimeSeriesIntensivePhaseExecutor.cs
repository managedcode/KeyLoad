using System.Diagnostics;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensivePhaseExecutor(ITimeSeriesIntensiveTarget target,
    TimeSeriesIntensiveExpectations expected, TimeSeriesIntensivePreparedPhase phase,
    TimeSeriesIntensiveAttemptLedger ledger, CancellationToken cellCancellation) : IAsyncDisposable
{
    private readonly TimeSeriesIntensivePhaseWorkers workers = new(cellCancellation);
    private readonly TimeSeriesIntensiveConcurrency concurrency = new();

    internal async Task<TimeSeriesIntensivePhaseResult> RunAsync()
    {
        for (var index = 0; index < TimeSeriesIntensiveProfile.Concurrency; index++)
        {
            workers.Add(WorkAsync(index));
        }

        await workers.Ready.ConfigureAwait(false);
        var started = Stopwatch.GetTimestamp();
        workers.Release();
        await workers.JoinAsync().ConfigureAwait(false);
        var wall = Stopwatch.GetTimestamp() - started;
        var measurement = !phase.Warmup && ledger.Complete
            ? TimeSeriesIntensiveSummary.Calculate(ledger.Attempts.Span, wall, Stopwatch.Frequency) : null;
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
                    index, worker, concurrency, workers.Token).ConfigureAwait(false);
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

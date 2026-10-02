using System.Diagnostics;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal sealed class TimeSeriesComparisonTargetExecutor(TimeSeriesComparisonWorkload workload,
    TimeSeriesComparisonRecorder recorder)
{
    internal async Task ExecuteAsync(ITimeSeriesPersistentTarget target, CancellationToken cancellationToken)
    {
        if (!await ExecuteSetupAsync(target, "initialize",
                () => target.InitializeAsync(workload, cancellationToken)))
        {
            return;
        }

        if (!await ExecuteSetupAsync(target, "seed", () => target.SeedAsync(workload, cancellationToken)) ||
            !await ExecuteSetupAsync(target, "idempotent-seed-retry",
                () => target.SeedAsync(workload, cancellationToken)))
        {
            return;
        }

        await ExecuteReadsAsync(target, cancellationToken);
        if (target is ITimeSeriesAggregationTarget aggregator)
        {
            await ExecuteAggregationAsync(target, aggregator, cancellationToken);
        }
    }

    internal async Task CleanupAsync(ITimeSeriesPersistentTarget target)
    {
        try
        {
            await target.DisposeAsync();
            recorder.RecordCleanup(target.Metadata.Name, true);
        }
        catch (InvalidOperationException error) when (TimeSeriesComparisonTargetErrors.TryGetCode(error, out _))
        {
            recorder.RecordCleanup(target.Metadata.Name, false, ErrorCode(error));
        }
    }

    private async Task<bool> ExecuteSetupAsync(ITimeSeriesPersistentTarget target, string operation,
        Func<Task> action)
    {
        try
        {
            await action();
            recorder.RecordSetup(target.Metadata.Name, operation, true);
            return true;
        }
        catch (InvalidOperationException error) when (TimeSeriesComparisonTargetErrors.TryGetCode(error, out _))
        {
            recorder.RecordSetup(target.Metadata.Name, operation, false, ErrorCode(error));
            return false;
        }
        catch (OperationCanceledException)
        {
            recorder.RecordSetup(target.Metadata.Name, operation, false, "Cancelled");
            return false;
        }
    }

    private async Task ExecuteReadsAsync(ITimeSeriesPersistentTarget target, CancellationToken cancellationToken)
    {
        foreach (var range in workload.ReadRanges)
        {
            var measure = range.ExpectedErrorCode is null;
            var started = measure ? Stopwatch.GetTimestamp() : 0;
            try
            {
                var result = await target.ReadAsync(workload, range, cancellationToken);
                double? elapsed = measure ? Stopwatch.GetElapsedTime(started).TotalMilliseconds : null;
                recorder.RecordRead(target.Metadata.Name, range, result, elapsed);
            }
            catch (InvalidOperationException error) when (TimeSeriesComparisonTargetErrors.TryGetCode(error, out _))
            {
                double? elapsed = measure ? Stopwatch.GetElapsedTime(started).TotalMilliseconds : null;
                recorder.RecordTargetFailure(target.Metadata.Name, range.Name, ErrorCode(error), elapsed);
            }
            catch (OperationCanceledException)
            {
                recorder.RecordTargetFailure(target.Metadata.Name, range.Name, "Cancelled");
                return;
            }
        }
    }

    private async Task ExecuteAggregationAsync(ITimeSeriesPersistentTarget target,
        ITimeSeriesAggregationTarget aggregator, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var buckets = await aggregator.AggregateAsync(workload, cancellationToken);
            recorder.RecordBuckets(target.Metadata.Name, buckets,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (InvalidOperationException error) when (TimeSeriesComparisonTargetErrors.TryGetCode(error, out _))
        {
            recorder.RecordTargetFailure(target.Metadata.Name, "bucket-sum-oracle", ErrorCode(error),
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            recorder.RecordTargetFailure(target.Metadata.Name, "bucket-sum-oracle", "Cancelled");
        }
    }

    private static string ErrorCode(InvalidOperationException error)
    {
        _ = TimeSeriesComparisonTargetErrors.TryGetCode(error, out var errorCode);
        return errorCode;
    }
}

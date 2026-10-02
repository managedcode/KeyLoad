namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal static class TimeSeriesComparisonRunner
{
    private const string LibraryName = "ManagedCode.TimeSeries";

    internal static async Task<int> RunAsync(ITimeSeriesPersistentTarget[] targets, string sourceRevision,
        string outputDirectory, CancellationToken cancellationToken, GitHubProvenance? provenance = null,
        string? loadGeneratorImage = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var workload = TimeSeriesComparisonWorkloadFactory.Create(Guid.NewGuid().ToString("N"));
        var recorder = new TimeSeriesComparisonRecorder(workload, TimeProvider.System);
        foreach (var target in targets)
        {
            recorder.AddTarget(target.Metadata);
        }

        recorder.AddLibraryTarget();
        await ExecuteTargetsAsync(targets, workload, recorder, cancellationToken);
        ExecuteLibraryAggregation(workload, recorder);
        var report = recorder.CreateReport(sourceRevision) with
        {
            Provenance = provenance,
            LoadGeneratorImage = loadGeneratorImage
        };
        await TimeSeriesComparisonReportWriter.WriteAsync(report, outputDirectory);
        return recorder.Passed ? 0 : 1;
    }

    private static async Task ExecuteTargetsAsync(ITimeSeriesPersistentTarget[] targets,
        TimeSeriesComparisonWorkload workload, TimeSeriesComparisonRecorder recorder,
        CancellationToken cancellationToken)
    {
        var executor = new TimeSeriesComparisonTargetExecutor(workload, recorder);
        try
        {
            foreach (var target in targets)
            {
                await executor.ExecuteAsync(target, cancellationToken);
            }
        }
        finally
        {
            await CleanupTargetsAsync(targets, executor, targets.Length - 1);
        }
    }

    private static async Task CleanupTargetsAsync(ITimeSeriesPersistentTarget[] targets,
        TimeSeriesComparisonTargetExecutor executor, int index)
    {
        if (index < 0)
        {
            return;
        }

        try
        {
            await executor.CleanupAsync(targets[index]);
        }
        finally
        {
            await CleanupTargetsAsync(targets, executor, index - 1);
        }
    }

    private static void ExecuteLibraryAggregation(TimeSeriesComparisonWorkload workload,
        TimeSeriesComparisonRecorder recorder)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var buckets = ManagedCodeTimeSeriesAggregation.Aggregate(workload);
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        recorder.RecordBuckets(LibraryName, buckets, elapsed);
    }
}

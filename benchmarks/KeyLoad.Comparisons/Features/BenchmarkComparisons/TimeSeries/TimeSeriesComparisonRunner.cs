using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal static class TimeSeriesComparisonRunner
{
    private const string RunIdentityFormat = "N";

    private const string LibraryName = "ManagedCode.TimeSeries";

    internal static async Task<int> RunAsync(ITimeSeriesPersistentTarget[] targets, string sourceRevision,
        string outputDirectory, IOptions<NativeComparisonExecutionOptions> executionOptions, CancellationToken cancellationToken, GitHubProvenance? provenance = null,
        string? loadGeneratorImage = null)
    {
        const int NoObservedItems = 0;
        const int SingleItemCount = 1;

        NativeComparisonExecutionOptions.Require(executionOptions);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var workload = TimeSeriesComparisonWorkloadFactory.Create(Guid.NewGuid().ToString(RunIdentityFormat));
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
        await TimeSeriesComparisonReportWriter.WriteAsync(report, outputDirectory, executionOptions);
        return recorder.Passed ? NoObservedItems : SingleItemCount;
    }

    private static async Task ExecuteTargetsAsync(ITimeSeriesPersistentTarget[] targets,
        TimeSeriesComparisonWorkload workload, TimeSeriesComparisonRecorder recorder,
        CancellationToken cancellationToken)
    {
        const int SingleItemCount = 1;

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
            await CleanupTargetsAsync(targets, executor, targets.Length - SingleItemCount);
        }
    }

    private static async Task CleanupTargetsAsync(ITimeSeriesPersistentTarget[] targets,
        TimeSeriesComparisonTargetExecutor executor, int index)
    {
        const int NoObservedItems = 0;
        const int AdjacentElementOffset = 1;

        if (index < NoObservedItems)
        {
            return;
        }

        try
        {
            await executor.CleanupAsync(targets[index]);
        }
        finally
        {
            await CleanupTargetsAsync(targets, executor, index - AdjacentElementOffset);
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

using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Globalization;

namespace KeyLoad.Comparisons;

internal static class OpenLoopExecutionCompletion
{
    internal static async Task SettleAsync(OpenLoopExecutionContext execution)
    {
        execution.State?.Freeze(Stopwatch.GetTimestamp());
        try
        {
            var close = await ComparisonSessionCleanup.CloseAndJoinAsync(execution.Sessions,
                TimeSpan.FromMilliseconds(execution.ExecutionPolicy.DrainMilliseconds))
                .ConfigureAwait(false);
            execution.SessionsClosed = close.Failures.IsEmpty && !close.ThresholdExpired;
            execution.CleanupFailures = close.ThresholdExpired
                ? close.Failures.Add(new ComparisonFailureException(ComparisonSessionCleanup.Failure))
                : close.Failures;
        }
        catch (Exception error)
        {
            execution.CleanupFailures = execution.CleanupFailures.Add(error);
        }
    }

    internal static OpenLoopComparisonReport CreateReport(OpenLoopExecutionContext execution, Action<string>? progress)
    {
        var state = execution.State ?? throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopMeasurementNotStarted);
        var snapshot = state.Snapshot(execution.Timeline.ElapsedSeconds(execution.MeasurementFinishedTimestamp));
        var timing = OpenLoopTimingSummarizer.Summarize(snapshot, OpenLoopRateContract.SampleCapacity);
        progress?.Invoke(string.Format(CultureInfo.CurrentCulture, OpenLoopEvidenceContract.ProgressMessageFormat,
            execution.Scenario, execution.Rate, snapshot.Accounting.Succeeded, snapshot.Accounting.Planned));
        var worker = execution.Worker;
        return new(OpenLoopEvidenceContract.SchemaVersion, worker.RunId, worker.Attempt, worker.JobId, execution.StartedAt,
            execution.Profile.Id, execution.Profile.Documents, execution.Rate, execution.Scenario,
            execution.Corpus.Sha256, execution.Target.Profile, worker.SourceRevision, execution.Storage,
            snapshot.ElapsedSeconds, execution.CallerCancelled, execution.DrainExpired,
            snapshot.Accounting.NotOffered == 0, execution.Readback, execution.SessionsClosed,
            snapshot.Accounting, timing, snapshot.Samples, execution.ExecutionPolicy)
        {
            Worker = worker,
            ClientResources = execution.Resources,
            Runtime = RuntimeInformation.FrameworkDescription
        };
    }

    internal static void ThrowUnlessMeasuredCancellation(Exception? failure, OpenLoopRunState? state,
        bool callerCancelled, CancellationToken cancellationToken)
    {
        if (failure is not null && !(state is not null && failure is OperationCanceledException error
            && error.CancellationToken == cancellationToken && callerCancelled
            && cancellationToken.IsCancellationRequested))
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

}

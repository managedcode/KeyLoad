using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveCallScope : IDisposable
{
    private readonly NativeComparisonExecutionOptions execution;
    private readonly CancellationToken cellCancellation;
    private readonly CancellationTokenSource deadline;
    private long started;

    internal TimeSeriesIntensiveCallScope(IOptions<NativeComparisonExecutionOptions> executionOptions, CancellationToken cellCancellation)
    {
        execution = executionOptions.Value;
        execution.Validate();
        this.cellCancellation = cellCancellation;
        deadline = CancellationTokenSource.CreateLinkedTokenSource(cellCancellation);
    }

    internal CancellationToken Token => deadline.Token;
    internal long LatencyTicks { get; private set; }
    internal TimeSeriesIntensiveOutcome Completion { get; private set; }

    internal bool AcceptInvocation() => !cellCancellation.IsCancellationRequested;

    internal void Start()
    {
        deadline.CancelAfter(execution.OperationTimeout);
        started = Stopwatch.GetTimestamp();
    }

    internal void Stop()
    {
        var stopped = Stopwatch.GetTimestamp();
        LatencyTicks = stopped - started;
        Completion = cellCancellation.IsCancellationRequested ? TimeSeriesIntensiveOutcome.Cancelled
            : deadline.IsCancellationRequested || Stopwatch.GetElapsedTime(started, stopped) >= execution.OperationTimeout
                ? TimeSeriesIntensiveOutcome.DeadlineExceeded : TimeSeriesIntensiveOutcome.Succeeded;
    }

    internal Exception ObserveFailure(Exception original)
    {
        ArgumentNullException.ThrowIfNull(original);
        Stop();
        return TimeSeriesIntensiveObservedFailureException.Observe(Completion, original);
    }

    internal void RequireSuccess()
    {
        if (Completion == TimeSeriesIntensiveOutcome.DeadlineExceeded)
        {
            throw new TimeSeriesIntensiveCallDeadlineException();
        }

        if (Completion == TimeSeriesIntensiveOutcome.Cancelled)
        {
            throw new OperationCanceledException(cellCancellation);
        }
    }

    public void Dispose() => deadline.Dispose();
}

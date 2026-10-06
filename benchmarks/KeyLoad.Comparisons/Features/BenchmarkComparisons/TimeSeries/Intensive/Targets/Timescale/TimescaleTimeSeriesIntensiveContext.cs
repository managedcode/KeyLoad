namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimescaleTimeSeriesIntensiveContext
{
    internal TimescaleTimeSeriesIntensiveContext(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        RunId = runId;
        SchemaName = TimescaleSchemaLifecycle.SchemaName(runId);
        TimescaleSchemaLifecycle.ValidateSchemaName(SchemaName);
        SeriesSet = TimeSeriesIntensiveProfile.Name;
        OwnerId = Guid.NewGuid();
    }

    private readonly System.Threading.Lock lifetimeGate = new();
    private TaskCompletionSource drained = CompletedSource();
    private int activeOperations;
    private bool closing;

    internal string RunId { get; }
    internal string SchemaName { get; }
    internal string SeriesSet { get; }
    internal Guid OwnerId { get; }

    internal IAsyncDisposable EnterOperation()
    {
        const int NoObservedItems = 0;

        lock (lifetimeGate)
        {
            if (closing)
            {
                throw new ObjectDisposedException(nameof(TimescaleTimeSeriesIntensiveContext),
                    TimescaleTimeSeriesIntensiveProtocol.ContextClosed);
            }

            if (activeOperations == NoObservedItems)
            {
                drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            activeOperations++;
            return new OperationLease(this);
        }
    }

    internal Task CloseAndDrainAsync()
    {
        const int NoObservedItems = 0;

        lock (lifetimeGate)
        {
            closing = true;
            return activeOperations == NoObservedItems ? Task.CompletedTask : drained.Task;
        }
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    private void ExitOperation()
    {
        const int NoObservedItems = 0;

        lock (lifetimeGate)
        {
            activeOperations--;
            if (closing && activeOperations == NoObservedItems)
            {
                drained.TrySetResult();
            }
        }
    }

    private sealed class OperationLease(TimescaleTimeSeriesIntensiveContext owner) : IAsyncDisposable
    {
        private TimescaleTimeSeriesIntensiveContext? context = owner;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref context, null)?.ExitOperation();
            return ValueTask.CompletedTask;
        }
    }
}

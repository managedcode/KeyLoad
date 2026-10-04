using System.Runtime.ExceptionServices;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed partial class TimescaleTimeSeriesIntensiveTarget : ITimeSeriesIntensiveTarget
{
    private readonly TimescaleTimeSeriesIntensiveContext context;
    private readonly TimescaleTimeSeriesIntensiveSession session;
    private readonly object disposeGate = new();
    private Task? disposeTask;
    private int closed;
    private int initialized;
    private int seedOrdinal;
    private bool ownsSchema;

    internal TimescaleTimeSeriesIntensiveTarget(string connectionString, string runId)
    {
        context = new(runId);
        session = new(connectionString, context.SchemaName);
    }

    internal TimescaleTimeSeriesIntensiveContext Context => context;
    internal TimescaleTimeSeriesIntensiveSession Session => session;
    internal bool OwnsSchema { get => ownsSchema; set => ownsSchema = value; }
    internal int ReadSeedOrdinal() => Volatile.Read(ref seedOrdinal);
    internal bool TryReserveSeedOrdinal(int batch) =>
        Interlocked.CompareExchange(ref seedOrdinal, -(batch + 1), batch) == batch;
    internal bool CompleteSeedOrdinal(int batch) =>
        Interlocked.CompareExchange(ref seedOrdinal, batch + 1, -(batch + 1)) == -(batch + 1);
    internal void ReleaseSeedOrdinal(int batch) => Interlocked.CompareExchange(ref seedOrdinal, batch, -(batch + 1));
    internal bool TryBeginInitialization() => Interlocked.CompareExchange(ref initialized, -1, 0) == 0;
    internal void MarkInitializationSucceeded() => Volatile.Write(ref initialized, 1);
    internal void MarkInitializationFailed() => Volatile.Write(ref initialized, 0);

    public ValueTask DisposeAsync()
    {
        lock (disposeGate)
        {
            if (disposeTask is null)
            {
                Interlocked.Exchange(ref closed, 1);
                disposeTask = DisposeCoreAsync();
            }
            return new ValueTask(disposeTask);
        }
    }

    internal void EnsureOpen()
    {
        if (Volatile.Read(ref closed) != 0)
        {
            throw new ObjectDisposedException(nameof(TimescaleTimeSeriesIntensiveTarget),
                TimescaleTimeSeriesIntensiveProtocol.ContextClosed);
        }
    }

    private async Task DisposeCoreAsync()
    {
        ExceptionDispatchInfo? cleanup = null;
        try
        {
            await context.CloseAndDrainAsync().ConfigureAwait(false);
            if (OwnsSchema)
            {
                using var timeout = new CancellationTokenSource(TimescaleTimeSeriesIntensiveProtocol.ConnectionTimeoutSeconds * 1000);
                await session.DropOwnedSchemaAsync(context, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
        {
            CaptureCleanup(ref cleanup, error);
        }
        catch (Exception error) when (!TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
        {
            CaptureCleanup(ref cleanup, error);
        }
        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
        {
            CaptureCleanup(ref cleanup, error);
        }
        catch (Exception error) when (!TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
        {
            CaptureCleanup(ref cleanup, error);
        }

        var joined = TimeSeriesIntensiveTargetCompletionException.Join(null, cleanup?.SourceException, null);
        if (joined is null)
        {
            return;
        }
        if (ReferenceEquals(joined, cleanup?.SourceException))
        {
            cleanup!.Throw();
        }
        ExceptionDispatchInfo.Capture(joined).Throw();
    }

    internal async ValueTask<TimescaleTimeSeriesIntensiveOperation> StartOperationAsync(CancellationToken token)
    {
        EnsureOpen();
        var operation = new TimescaleTimeSeriesIntensiveOperation();
        operation.Own(context.EnterOperation());
        try
        {
            if (Volatile.Read(ref initialized) != 1)
            {
                throw new ComparisonFailureException(TimescaleTimeSeriesIntensiveProtocol.InvalidInitialization);
            }
            var connection = operation.Own(session.CreateConnection());
            await connection.OpenAsync(token).ConfigureAwait(false);
            return operation.OwnConnection(connection);
        }
        catch (Exception error)
        {
            operation.RecordPrimary(error);
            await operation.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static void CaptureCleanup(ref ExceptionDispatchInfo? cleanup, Exception error)
    {
        if (cleanup is null || TimeSeriesIntensiveExceptionBoundary.IsNonfatal(cleanup.SourceException)
            && !TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
        {
            cleanup = ExceptionDispatchInfo.Capture(error);
        }
    }

}

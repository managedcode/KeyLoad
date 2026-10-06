using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed partial class TimescaleTimeSeriesIntensiveTarget : ITimeSeriesIntensiveTarget
{
    private readonly TimeProvider timeProvider;
    private const int SingleItemCount = 1;
    private const int AdjacentElementOffset = 1;
    private const int MissingItemIndex = -1;
    private const int NoObservedItems = 0;

    private readonly NativeComparisonExecutionOptions execution;

    private readonly TimescaleTimeSeriesIntensiveContext context;
    private readonly TimescaleTimeSeriesIntensiveSession session;
    private readonly System.Threading.Lock disposeGate = new();
    private Task? disposeTask;
    private int closed;
    private int initialized;
    private int seedOrdinal;
    private bool ownsSchema;

    internal TimescaleTimeSeriesIntensiveTarget(string connectionString, string runId, IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider? provider = null)
    {
        timeProvider = provider ?? TimeProvider.System;
        execution = executionOptions.Value;
        execution.Validate();
        context = new(runId);
        session = new(connectionString, context.SchemaName, executionOptions);
    }

    internal TimescaleTimeSeriesIntensiveContext Context => context;
    internal TimescaleTimeSeriesIntensiveSession Session => session;
    internal bool OwnsSchema { get => ownsSchema; set => ownsSchema = value; }
    internal int ReadSeedOrdinal() => Volatile.Read(ref seedOrdinal);
    internal bool TryReserveSeedOrdinal(int batch) =>
        Interlocked.CompareExchange(ref seedOrdinal, -(batch + SingleItemCount), batch) == batch;
    internal bool CompleteSeedOrdinal(int batch) =>
        Interlocked.CompareExchange(ref seedOrdinal, batch + SingleItemCount, -(batch + SingleItemCount)) == -(batch + AdjacentElementOffset);
    internal void ReleaseSeedOrdinal(int batch) => Interlocked.CompareExchange(ref seedOrdinal, batch, -(batch + SingleItemCount));
    internal bool TryBeginInitialization() => Interlocked.CompareExchange(ref initialized, MissingItemIndex, NoObservedItems) == NoObservedItems;
    internal void MarkInitializationSucceeded() => Volatile.Write(ref initialized, SingleItemCount);
    internal void MarkInitializationFailed() => Volatile.Write(ref initialized, NoObservedItems);

    public ValueTask DisposeAsync()
    {
        const int SingleItemCount = 1;

        lock (disposeGate)
        {
            if (disposeTask is null)
            {
                Interlocked.Exchange(ref closed, SingleItemCount);
                disposeTask = DisposeCoreAsync();
            }
            return new ValueTask(disposeTask);
        }
    }

    internal void EnsureOpen()
    {
        const int NoObservedItems = 0;

        if (Volatile.Read(ref closed) != NoObservedItems)
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
                using var timeout = new CancellationTokenSource(execution.TimescaleConnectionTimeout, timeProvider);
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
        const int SingleItemCount = 1;

        EnsureOpen();
        var operation = new TimescaleTimeSeriesIntensiveOperation();
        operation.Own(context.EnterOperation());
        try
        {
            if (Volatile.Read(ref initialized) != SingleItemCount)
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

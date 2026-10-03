using System.Runtime.ExceptionServices;
using Npgsql;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimescaleTimeSeriesIntensiveOperation : IAsyncDisposable
{
    private readonly IAsyncDisposable?[] resources = new IAsyncDisposable?[5];
    private ExceptionDispatchInfo? primary;
    private ExceptionDispatchInfo? cleanup;
    private int resourceCount;
    private bool finished;

    internal TimeSeriesIntensiveAcknowledgement? Acknowledgement { get; set; }
    internal NpgsqlConnection Connection { get; private set; } = null!;
    internal Exception? CleanupException => cleanup?.SourceException;

    internal TimescaleTimeSeriesIntensiveOperation OwnConnection(NpgsqlConnection connection)
    {
        Connection = connection;
        return this;
    }

    internal T Own<T>(T resource) where T : IAsyncDisposable
    {
        ArgumentNullException.ThrowIfNull(resource);
        resources[resourceCount++] = resource;
        return resource;
    }

    internal TimescaleTimeSeriesIntensiveCommand CreateCommand(TimescaleTimeSeriesIntensiveRoutine routine,
        NpgsqlTransaction? transaction = null)
    {
        if (resourceCount == resources.Length)
        {
            throw new InvalidOperationException(TimescaleTimeSeriesIntensiveProtocol.InvalidContext);
        }
        var resource = new TimescaleTimeSeriesIntensiveCommand(routine, Connection, transaction);
        resources[resourceCount++] = resource;
        return resource;
    }

    internal void RecordPrimary(Exception error)
    {
        if (cleanup?.SourceException == error)
        {
            return;
        }
        primary ??= ExceptionDispatchInfo.Capture(error);
    }

    internal async ValueTask DisposeResourceAsync(IAsyncDisposable resource)
    {
        for (var index = resourceCount - 1; index >= 0; index--)
        {
            if (ReferenceEquals(resources[index], resource))
            {
                resources[index] = null;
                await DisposeOneAsync(resource).ConfigureAwait(false);
                return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (finished)
        {
            return;
        }
        finished = true;
        for (var index = resourceCount - 1; index >= 0; index--)
        {
            var resource = resources[index];
            resources[index] = null;
            if (resource is not null)
            {
                await DisposeOneAsync(resource).ConfigureAwait(false);
            }
        }

        var joined = TimeSeriesIntensiveTargetCompletionException.Join(primary?.SourceException,
            cleanup?.SourceException, Acknowledgement);
        if (joined is null)
        {
            return;
        }
        if (ReferenceEquals(joined, primary?.SourceException))
        {
            primary!.Throw();
        }
        if (ReferenceEquals(joined, cleanup?.SourceException))
        {
            cleanup!.Throw();
        }
        ExceptionDispatchInfo.Capture(joined).Throw();
    }

    private async ValueTask DisposeOneAsync(IAsyncDisposable resource)
    {
        try
        {
            await resource.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
        {
            CaptureCleanup(error);
        }
        catch (Exception error) when (!TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
        {
            CaptureCleanup(error);
        }
    }

    private void CaptureCleanup(Exception error)
    {
        if (cleanup is null || TimeSeriesIntensiveExceptionBoundary.IsNonfatal(cleanup.SourceException)
            && !TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
        {
            cleanup = ExceptionDispatchInfo.Capture(error);
        }
    }
}

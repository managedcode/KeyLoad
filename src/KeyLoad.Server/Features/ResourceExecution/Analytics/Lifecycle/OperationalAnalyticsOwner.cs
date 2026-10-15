using System.Runtime.ExceptionServices;
using KeyLoad.Clustering;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;
using ZoneTree.Analytics;

namespace KeyLoad.Server;

internal sealed class OperationalAnalyticsOwner : IAsyncDisposable
{
    private readonly Lock lifecycle = new();
    private readonly AnalyticsEngine engine;
    private readonly OperationalAnalyticsNativeLogger failures = new();
    private readonly OperationalAnalyticsOptions settings;
    private readonly TimeProvider clock;
    private Task? operation;
    private Task? shutdown;
    private ExceptionDispatchInfo? originalFailure;
    private OperationCanceledException? callerCancellation;
    private bool operationCallerCancelled;
    private bool closed;
    internal bool HasJoinedEngine
    {
        get;
        private set;
    }
    internal OperationCanceledException? ObservedCallerCancellation => Volatile.Read(ref callerCancellation);

    internal OperationalAnalyticsOwner(string nodeDirectory, IOptions<OperationalAnalyticsOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        settings = options.Value;
        if (!settings.IsValid())
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(OperationalAnalyticsOptions),
                [OperationalAnalyticsProtocol.InvalidPolicy]);
        }
        if (!ReferenceEquals(clock, TimeProvider.System))
        {
            throw new ArgumentException(OperationalAnalyticsProtocol.InvalidPolicy, nameof(clock));
        }
        this.clock = clock;
        engine = new AnalyticsEngine(OperationalAnalyticsNativeConfiguration.Create(nodeDirectory, settings, failures));
    }

    internal bool TryRecord(ReplicaNodeState snapshot)
    {
        lock (lifecycle)
        {
            RequireAdmission();
            return engine.Track(OperationalAnalyticsSample.Create(snapshot, clock.GetUtcNow()));
        }
    }

    internal Task FlushAsync(CancellationToken token)
        => BeginAsync(async () =>
        {
            await engine.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            failures.ThrowIfFailed();
            token.ThrowIfCancellationRequested();
            return true;
        }, token);

    internal Task<OperationalAnalyticsReport> ReadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        OperationalAnalyticsQuery.Validate(from, to, clock.GetUtcNow(), settings);
        return BeginAsync(async () =>
        {
            await engine.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var result = await engine.QueryAsync(OperationalAnalyticsQuery.Create(from, to), token).ConfigureAwait(false);
            failures.ThrowIfFailed();
            return OperationalAnalyticsReportProjection.Create(result, engine.GetStatistics(false), settings);
        }, token);
    }

    private Task<T> BeginAsync<T>(Func<Task<T>> start, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (lifecycle)
        {
            RequireAdmission();
            if (operation is { IsCompleted: false })
            {
                throw new InvalidOperationException(OperationalAnalyticsProtocol.Busy);
            }
            operationCallerCancelled = false;
            var original = ObserveOriginalAsync(start, token);
            operation = original;
            return original.WaitAsync(token);
        }
    }

    private async Task<T> ObserveOriginalAsync<T>(Func<Task<T>> start, CancellationToken token)
    {
        try
        {
            return await start().ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (token.IsCancellationRequested)
        {
            lock (lifecycle)
            {
                operationCallerCancelled = true;
                callerCancellation = error;
            }
            throw;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            Interlocked.CompareExchange(ref originalFailure, ExceptionDispatchInfo.Capture(error), null);
            throw;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            Interlocked.CompareExchange(ref originalFailure, ExceptionDispatchInfo.Capture(error), null);
            throw;
        }
    }

    internal void CloseAdmission()
    {
        lock (lifecycle)
        {
            closed = true;
        }
    }

    internal void RequireHealthy()
    {
        lock (lifecycle)
        {
            RequireAdmission();
        }
    }

    private void RequireAdmission()
    {
        ObjectDisposedException.ThrowIf(closed, this);
        failures.ThrowIfFailed();
        Volatile.Read(ref originalFailure)?.Throw();
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            closed = true;
            shutdown ??= CloseAsync(operation ?? Task.CompletedTask);
            return new(shutdown);
        }
    }

    private async Task CloseAsync(Task original)
    {
        var errors = new List<Exception>();
        await original.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (!original.IsCanceled || !operationCallerCancelled)
        {
            await ServerFailureObserver.ObserveAsync(() => original, errors).ConfigureAwait(false);
        }
        try
        {
            await engine.DisposeAsync().ConfigureAwait(false);
            HasJoinedEngine = true;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            errors.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            errors.Add(error);
        }
        if (!HasJoinedEngine)
        {
            await RetryNativeCloseAsync(errors).ConfigureAwait(false);
        }
        ServerFailureObserver.Observe(failures.ThrowIfFailed, errors);
        ServerFailureObserver.ThrowIfAny(errors);
    }

    private async Task RetryNativeCloseAsync(List<Exception> errors)
    {
        try
        {
            await engine.DisposeAsync().ConfigureAwait(false);
            HasJoinedEngine = true;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            errors.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            errors.Add(error);
        }
    }
}

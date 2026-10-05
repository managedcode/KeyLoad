using System.Runtime.ExceptionServices;

namespace KeyLoad.Orleans;

/// <summary>Stops admission, cancels admitted work and releases resources after the last owner exits.</summary>
internal sealed class ReplicaDiscoveryLifetime : IAsyncDisposable
{
    private readonly Lock sync = new();
    private readonly CancellationToken stoppingToken;
    private readonly Func<Task> cancelStopping;
    private readonly Action? releaseResources;
    private readonly ReplicaDiscoveryResources? ownedResources;
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource startShutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int active;
    private bool shutdownStarted;
    private Task? shutdownWork;

    internal ReplicaDiscoveryLifetime(CancellationTokenSource stopping, Action releaseResources)
    {
        ArgumentNullException.ThrowIfNull(stopping);
        ArgumentNullException.ThrowIfNull(releaseResources);
        cancelStopping = stopping.CancelAsync;
        stoppingToken = stopping.Token;
        this.releaseResources = releaseResources;
    }

    internal ReplicaDiscoveryLifetime(ReplicaDiscoveryResources resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ownedResources = resources;
        stoppingToken = resources.StoppingSource.Token;
        cancelStopping = resources.StoppingSource.CancelAsync;
    }

    internal bool IsStopping
    {
        get
        {
            lock (sync)
            {
                return shutdownStarted;
            }
        }
    }

    internal Operation? TryEnter()
    {
        lock (sync)
        {
            if (shutdownStarted)
            {
                return null;
            }

            active++;
            return new Operation(this, stoppingToken);
        }
    }

    internal Task StopAsync()
    {
        Task work;
        lock (sync)
        {
            if (shutdownWork is not null)
            {
                return shutdownWork;
            }

            shutdownStarted = true;
            if (active == 0)
            {
                drained.TrySetResult();
            }

            shutdownWork = StopCoreAsync();
            work = shutdownWork;
        }

        startShutdown.TrySetResult();
        return work;
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task StopCoreAsync()
    {
        await startShutdown.Task.ConfigureAwait(false);
        var failures = new List<Exception>(4);
        await ObserveAsync(cancelStopping, failures).ConfigureAwait(false);
        await ObserveAsync(() => drained.Task, failures).ConfigureAwait(false);
        if (ownedResources is not null)
        {
            DisposeOwnedResources(failures);
        }
        else
        {
            ObserveCleanup(releaseResources!, failures);
        }
        ThrowIfAny(failures);
    }

    private void DisposeOwnedResources(List<Exception> failures)
    {
        try
        {
            ownedResources!.Dispose();
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
    }

    private static async Task ObserveAsync(Func<Task> stage, List<Exception> failures)
    {
        try
        {
            await stage().ConfigureAwait(false);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
    }

    internal static void ObserveCleanup(Action stage, List<Exception> failures)
    {
        try
        {
            stage();
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
    }

    private void Exit()
    {
        lock (sync)
        {
            active--;
            if (active == 0 && shutdownStarted)
            {
                drained.TrySetResult();
            }
        }
    }

    internal static void ThrowIfAny(List<Exception> failures)
    {
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }
    }

    internal sealed class Operation : IDisposable
    {
        private ReplicaDiscoveryLifetime? owner;

        internal Operation(ReplicaDiscoveryLifetime owner, CancellationToken shutdownToken)
        {
            this.owner = owner;
            ShutdownToken = shutdownToken;
        }

        internal CancellationToken ShutdownToken { get; }

        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Exit();
    }
}

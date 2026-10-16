using KeyLoad.Replication;

namespace KeyLoad.Clustering;

/// <summary>Drains only the original attached replica owners after membership shutdown.</summary>
public sealed class ReplicaTransportLifecycle : ILifecycleParticipant<ISiloLifecycle>
{
    private const int MaximumShutdownStages = 3;
    private const string AlreadyAttached = "The original replica transport owners are already attached or stopping.";
    private readonly Lock gate = new();
    private IReplicaEndpoint? endpoint;
    private ReplicaSiloDiscoveryState? discovery;
    private ReplicaNativeDiscoveryRuntime? nativeDiscovery;
    private Task? shutdown;

    /// <summary>Captures actual acquired owners before transport attachment, without resolving new services.</summary>
    public void Attach(IReplicaEndpoint endpoint, ReplicaSiloDiscoveryState discovery,
        ReplicaNativeDiscoveryRuntime nativeDiscovery)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(nativeDiscovery);
        lock (gate)
        {
            if (this.endpoint is not null || shutdown is not null)
            {
                throw new InvalidOperationException(AlreadyAttached);
            }
            this.endpoint = endpoint;
            this.discovery = discovery;
            this.nativeDiscovery = nativeDiscovery;
        }
    }

    /// <summary>Registers the mandatory original drain after membership teardown, independent of framework escalation.</summary>
    public void Participate(ISiloLifecycle lifecycle) => lifecycle.Subscribe(ReplicaTransportProtocol.LifecycleName,
        ServiceLifecycleStage.RuntimeStorageServices, _ => Task.CompletedTask,
        _ => StopAsync(CancellationToken.None));

    /// <summary>Returns the same original captured-owner drain for native and later physical-close joins.</summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return shutdown ??= StopCoreAsync(endpoint, discovery, nativeDiscovery, cancellationToken);
        }
    }

    private static async Task StopCoreAsync(IReplicaEndpoint? endpoint, ReplicaSiloDiscoveryState? discovery,
        ReplicaNativeDiscoveryRuntime? nativeDiscovery, CancellationToken cancellationToken)
    {
        // Publish the one original task before any native cancellation callback can reenter.
        await Task.Yield();
        if (endpoint is null || discovery is null || nativeDiscovery is null)
        {
            return;
        }
        var failures = new List<Exception>(MaximumShutdownStages);
        ReplicaDiscoveryLifetime.ObserveCleanup(discovery.StopDiscovery, failures);
        await ObserveAsync(nativeDiscovery.StopAsync, failures).ConfigureAwait(false);
        await StopEndpointAsync(endpoint, failures, cancellationToken).ConfigureAwait(false);
        ReplicaDiscoveryLifetime.ThrowIfAny(failures);
    }

    private static async Task StopEndpointAsync(IReplicaEndpoint endpoint, List<Exception> failures,
        CancellationToken cancellationToken)
    {
        Task original;
        try
        {
            original = endpoint.StopAsync(CancellationToken.None);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
            return;
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
            return;
        }
        var wait = original.WaitAsync(cancellationToken);
        try
        {
            await wait.ConfigureAwait(false);
        }
        catch (OperationCanceledException failure) when (cancellationToken.IsCancellationRequested
            && failure.CancellationToken == cancellationToken && !ReferenceEquals(wait, original))
        {
            failures.Add(failure);
            await ObserveAsync(() => original, failures).ConfigureAwait(false);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            await ObserveAsync(() => original, failures).ConfigureAwait(false);
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            await ObserveAsync(() => original, failures).ConfigureAwait(false);
        }
    }

    private static async Task ObserveAsync(Func<Task> stop, List<Exception> failures)
    {
        Task? original = null;
        try
        {
            original = stop();
            await original.ConfigureAwait(false);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            AddOriginal(original, error, failures);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            AddOriginal(original, error, failures);
        }
    }

    private static void AddOriginal(Task? original, Exception error, List<Exception> failures)
    {
        if (original?.Exception is { } terminal)
        {
            failures.AddRange(terminal.InnerExceptions);
        }
        else
        {
            failures.Add(error);
        }
    }
}

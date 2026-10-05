using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal sealed class NativeJobLifecycleParticipant(ILifecycleParticipant<ISiloLifecycle> native,
    NativeRequestWorkOwner requests) : ILifecycleParticipant<ISiloLifecycle>
{
    public void Participate(ISiloLifecycle lifecycle)
    {
        var forwarding = new NativeJobLifecycleForwarder(lifecycle, requests);
        native.Participate(forwarding);
        forwarding.RequireSingleSubscription();
    }
}

internal sealed class NativeJobLifecycleForwarder(ISiloLifecycle lifecycle, NativeRequestWorkOwner requests)
    : ISiloLifecycle
{
    private int subscriptions;
    public int HighestCompletedStage => lifecycle.HighestCompletedStage;
    public int LowestStoppedStage => lifecycle.LowestStoppedStage;

    public IDisposable Subscribe(string observerName, int stage, ILifecycleObserver observer)
    {
        if (stage != ServiceLifecycleStage.Active || ++subscriptions != 1)
        {
            throw new InvalidOperationException("The native job lifecycle must have exactly one Active observer.");
        }
        return lifecycle.Subscribe(observerName, stage, new NativeJobShutdownObserver(observer, requests));
    }

    internal void RequireSingleSubscription()
    {
        if (subscriptions != 1)
        {
            throw new InvalidOperationException("The native job lifecycle did not register its Active observer.");
        }
    }
}

internal sealed class NativeJobShutdownObserver(ILifecycleObserver native, NativeRequestWorkOwner requests)
    : ILifecycleObserver
{
    public Task OnStart(CancellationToken cancellationToken = default) => native.OnStart(cancellationToken);

    public async Task OnStop(CancellationToken cancellationToken = default)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => native.OnStop(cancellationToken), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(requests.DrainAsync, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

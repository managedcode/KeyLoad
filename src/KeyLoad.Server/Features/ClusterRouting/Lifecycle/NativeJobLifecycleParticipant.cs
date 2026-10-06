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
    private const int SingleSubscription = 1;
    private const string ExactObserverRequired = "The native job lifecycle must have exactly one Active observer.";
    private const string MissingObserver = "The native job lifecycle did not register its Active observer.";
    private int subscriptions;
    public int HighestCompletedStage => lifecycle.HighestCompletedStage;
    public int LowestStoppedStage => lifecycle.LowestStoppedStage;

    public IDisposable Subscribe(string observerName, int stage, ILifecycleObserver observer)
    {
        if (stage != ServiceLifecycleStage.Active || ++subscriptions != SingleSubscription)
        {
            throw new InvalidOperationException(ExactObserverRequired);
        }
        return lifecycle.Subscribe(observerName, stage, new NativeJobShutdownObserver(observer, requests));
    }

    internal void RequireSingleSubscription()
    {
        if (subscriptions != SingleSubscription)
        {
            throw new InvalidOperationException(MissingObserver);
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

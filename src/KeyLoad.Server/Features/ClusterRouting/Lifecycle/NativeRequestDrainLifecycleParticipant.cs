using KeyLoad.Clustering;

namespace KeyLoad.Server;

/// <summary>Joins the original silo request owner after native jobs and before lower storage services stop.</summary>
internal sealed class NativeRequestDrainLifecycleParticipant(NativeRequestWorkOwner requests)
    : ILifecycleParticipant<ISiloLifecycle>, ILifecycleObserver
{
    public void Participate(ISiloLifecycle lifecycle)
        => lifecycle.Subscribe(nameof(NativeRequestDrainLifecycleParticipant),
            ServiceLifecycleStage.ApplicationServices, this);

    public Task OnStart(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task OnStop(CancellationToken cancellationToken = default) => requests.DrainAsync();
}

using KeyLoad.Clustering;

namespace KeyLoad.Server;

/// <summary>Joins the original silo request owner after execution jobs and before lower storage services stop.</summary>
internal sealed class RequestDrainLifecycleParticipant(RequestWorkOwner requests)
    : ILifecycleParticipant<ISiloLifecycle>, ILifecycleObserver
{
    public void Participate(ISiloLifecycle lifecycle)
        => lifecycle.Subscribe(nameof(RequestDrainLifecycleParticipant),
            ServiceLifecycleStage.ApplicationServices, this);

    public Task OnStart(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task OnStop(CancellationToken cancellationToken = default) => requests.DrainAsync();
}

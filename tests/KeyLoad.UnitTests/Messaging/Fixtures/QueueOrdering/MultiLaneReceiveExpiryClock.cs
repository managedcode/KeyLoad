using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.Messaging;

/// <summary>Expires the parent after its real first child has returned and joined.</summary>
internal sealed class MultiLaneReceiveExpiryClock : TimeProvider, IGrainRequestPhaseObserver, RuntimeIGrainRequestPhaseObserver
{
    private readonly Lock gate = new();
    private DateTimeOffset initial = TimeProvider.System.GetUtcNow();
    private Func<long>? position;
    private Guid commandId;
    private long armed;
    private bool advanced;
    internal long ObservedPosition { get; private set; }

    internal void Arm(Func<long> runtimePosition, long committedCut, Guid firstCommandId)
    {
        lock (gate)
        {
            initial = TimeProvider.System.GetUtcNow();
            position = runtimePosition;
            armed = committedCut;
            commandId = firstCommandId;
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (gate)
        {
            return position is null ? TimeProvider.System.GetUtcNow()
                : advanced ? initial.AddMinutes(2) : initial;
        }
    }

    public ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        IGrainContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask RuntimeObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        SiloStorageIdentityProvider actualRuntime, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context)
    {
        lock (gate)
        {
            if (!advanced && identity.CommandId == commandId && context.GrainInstance is ConnectionGrain
                && position is { } observe && observe() > armed)
            {
                ObservedPosition = observe();
                advanced = true;
            }
        }
    }
}

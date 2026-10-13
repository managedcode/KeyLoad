using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class TenantPlacementNativeRuntime : IAsyncDisposable
{
    private readonly OwnerCapacityNativeLifetime capacity;
    private readonly PlacementIntentAdmissions admissions;
    private readonly PartitionMovementPeerAdmission peer;
    private readonly PartitionMovementRuntime movement;
    private readonly Lock closing = new();
    private Task? shutdown;

    internal TenantPlacementNativeRuntime(OwnerCapacityNativeLifetime capacity,
        PlacementIntentAdmissions admissions, PartitionMovementPeerAdmission peer,
        PartitionMovementRuntime movement, TenantPlacementPlanner planner,
        PlacementIntentExecutor executor, IPlacementIntentPersistence persistence, bool isControl)
    {
        this.capacity = capacity;
        this.admissions = admissions;
        this.peer = peer;
        this.movement = movement;
        Planner = planner;
        Executor = executor;
        Persistence = persistence;
        IsControl = isControl;
    }

    private bool IsControl { get; }
    internal ITenantPlacementPlanner Planner { get; }
    internal IPlacementIntentExecutor Executor { get; }
    internal IPlacementIntentPersistence Persistence { get; }
    internal OwnerCapacityNativeLifetime Capacity => capacity;
    internal void Start() => capacity.Start();

    internal void RegisterBorrowed(IServiceCollection services)
    {
        services.AddSingleton(capacity);
        if (IsControl)
        {
            services.AddSingleton(Planner);
            services.AddSingleton(Executor);
            services.AddSingleton(Persistence);
        }
    }

    internal async Task CloseAdmissionAsync()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(admissions.CloseAdmission, failures);
        await ServerFailureObserver.ObserveAsync(() => capacity.DisposeAsync().AsTask(), failures)
            .ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public ValueTask DisposeAsync()
    {
        lock (closing)
        { return new(shutdown ??= StopCoreAsync()); }
    }

    private async Task StopCoreAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(CloseAdmissionAsync, failures).ConfigureAwait(false);
        // The node joins the original movement owner before retiring this borrowed bundle.
        if (!movement.IsJoined)
        { failures.Add(Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RequestWorkNotJoined)); }
        else
        {
            await ServerFailureObserver.ObserveAsync(() => admissions.DisposeAsync().AsTask(), failures)
                .ConfigureAwait(false);
            ServerFailureObserver.Observe(peer.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

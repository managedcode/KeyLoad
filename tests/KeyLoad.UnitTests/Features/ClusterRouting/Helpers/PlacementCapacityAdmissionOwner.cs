using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PlacementCapacityAdmissionOwner : IAsyncDisposable
{
    internal PlacementIntentAdmissions Admission
    {
        get;
    } = new(
        Options.Create(new TenantPlacementExecutionOptions { Enabled = true }));
    internal List<PlacementIntentAdmissionLease> Leases { get; } = [];
    internal List<Task<PlacementIntentAdmissionLease>> Waiting { get; } = [];
    internal HashSet<Task<PlacementIntentAdmissionLease>> Observed { get; } = [];
    internal List<CancellationTokenSource> Callers { get; } = [];
    internal Task? Shutdown
    {
        get; set;
    }

    internal async Task ActiveAsync()
    {
        Leases.Add(await Admission.EnterAsync(CancellationToken.None));
        Leases.Add(await Admission.EnterAsync(CancellationToken.None));
    }

    internal void Queue()
    {
        var caller = new CancellationTokenSource();
        Callers.Add(caller);
        Waiting.Add(Admission.EnterAsync(caller.Token));
    }

    internal async Task JoinAsync(Task<PlacementIntentAdmissionLease> original, List<Exception> failures)
    {
        await ServerFailureObserver.ObserveAsync(async () => { Leases.Add(await original); }, failures);
        Observed.Add(original);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(Admission.CloseAdmission, failures);
        foreach (var lease in Leases)
        {
            ServerFailureObserver.Observe(lease.Dispose, failures);
        }
        foreach (var original in Waiting.Where(item => !Observed.Contains(item)))
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var lease = await original;
                ServerFailureObserver.Observe(lease.Dispose, failures);
            }, failures);
        }
        if (Shutdown is { } shutdown)
        {
            await ServerFailureObserver.ObserveAsync(() => shutdown, failures);
        }
        else
        {
            await ServerFailureObserver.ObserveAsync(() => Admission.DisposeAsync().AsTask(), failures);
        }
        foreach (var caller in Callers)
        {
            ServerFailureObserver.Observe(caller.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

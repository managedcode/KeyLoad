using System.Threading.RateLimiting;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PlacementIntentAdmissionLease : IDisposable
{
    private readonly PlacementIntentAdmissions owner;
    private RateLimitLease? native;

    internal PlacementIntentAdmissionLease(PlacementIntentAdmissions owner, RateLimitLease native)
    {
        this.owner = owner;
        this.native = native;
    }

    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref native, null);
        if (owned is null)
        {
            return;
        }
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(owned.Dispose, failures);
        ServerFailureObserver.Observe(owner.Release, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

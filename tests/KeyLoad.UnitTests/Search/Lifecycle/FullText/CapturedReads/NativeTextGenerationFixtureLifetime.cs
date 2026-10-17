using KeyLoad.Query.Features.Search;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextGenerationFixtureLifetime
{
    private readonly List<Exception> failures = [];
    private readonly List<ITextProjectionLease> leases = [];
    private readonly HashSet<ITextProjectionLease> settledLeases = new(ReferenceEqualityComparer.Instance);
    private readonly List<NativeTextProjection> projections = [];
    private readonly HashSet<NativeTextProjection> shutdownProjections = new(ReferenceEqualityComparer.Instance);

    internal NativeTextProjection TrackProjection(NativeTextProjection projection)
    {
        projections.Add(projection);
        return projection;
    }

    internal ITextProjectionLease TrackLease(ITextProjectionLease lease)
    {
        leases.Add(lease);
        return lease;
    }

    internal void SettleLease(ITextProjectionLease lease)
    {
        if (settledLeases.Add(lease))
        {
            lease.Dispose();
        }
    }

    internal void ShutdownProjection(NativeTextProjection projection)
    {
        if (!shutdownProjections.Contains(projection))
        {
            projection.Dispose();
            shutdownProjections.Add(projection);
        }
    }

    internal async Task RunAsync(Func<Task> body)
    {
        await ServerFailureObserver.ObserveAsync(body, failures);
        SettleRemainingLeases();
        ShutdownRemainingProjections();
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void SettleRemainingLeases()
    {
        for (var index = leases.Count - 1; index >= 0; index--)
        {
            var lease = leases[index];
            if (settledLeases.Add(lease))
            {
                ServerFailureObserver.Observe(lease.Dispose, failures);
            }
        }
    }

    private void ShutdownRemainingProjections()
    {
        for (var index = projections.Count - 1; index >= 0; index--)
        {
            var projection = projections[index];
            if (!shutdownProjections.Contains(projection))
            {
                ServerFailureObserver.Observe(() => ShutdownProjection(projection), failures);
            }
        }
    }
}

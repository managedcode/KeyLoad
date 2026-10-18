using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeBoundLeaseDisposalCleanup
{
    internal static async Task JoinAsync(NativeBoundLeaseSession session,
        Exception? expectedCleanup, List<Exception> errors)
    {
        if (session.Parent is { } parent)
        {
            await ServerFailureObserver.ObserveAsync(() =>
                CloseAsync(parent, expectedCleanup, errors.FirstOrDefault()), errors);
        }
        if (session.Owner is { } owner)
        {
            await ServerFailureObserver.ObserveAsync(() => owner.DisposeAsync().AsTask(), errors);
        }
        if (session.Ledger is { } ledger && session.Owner is { IsJoined: true })
        {
            ServerFailureObserver.Observe(ledger.Dispose, errors);
        }
        if (session.Linked is { } linked)
        {
            ServerFailureObserver.Observe(linked.Dispose, errors);
        }
    }

    private static async Task CloseAsync(NativeRequestWorkLease lease,
        Exception? expectedCleanup, Exception? original)
    {
        lease.PrepareDisposal(original, null);
        await lease.DisposeAsync();
        var outcome = await lease.Settlement;
        var unexpected = outcome.CleanupFailures.Where(error => !ReferenceEquals(error, expectedCleanup)).ToArray();
        if (unexpected.Length > 0)
        {
            throw new AggregateException(unexpected);
        }
    }
}

using Aspire.Hosting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Joins the original application stop and disposal under its unchanged cleanup deadline.</summary>
internal static class TwoRf3ApplicationShutdown
{
    internal static async Task<bool> JoinAsync(DistributedApplication owned, List<Exception> failures)
    {
        var before = failures.Count;
        using var deadline = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(() => owned.StopAsync(deadline.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        return failures.Count == before;
    }
}

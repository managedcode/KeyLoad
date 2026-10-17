using KeyLoad.Clustering;
using KeyLoad.Server;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

/// <summary>Joins the original cohort's admitted work and native transport before owned storage disposal.</summary>
internal static class RequestCqrsCohortShutdown
{
    internal static async Task StopAsync(TestCluster cluster, IEnumerable<RequestCqrsCohortNativeNode> actualNodes,
        Action release, TimeSpan shutdownBound)
    {
        release();
        var nodes = actualNodes.ToArray();
        using var deadline = new CancellationTokenSource(shutdownBound, TimeProvider.System);
        var failures = new List<Exception>();
        foreach (var node in nodes)
        {
            node.Observation.ReleaseAll();
            if (node.Hosted is { } hosted)
            {
                await ServerFailureObserver.ObserveAsync(hosted.JoinRequestsAsync, failures);
            }
        }
        if (nodes.Any(node => node.Hosted is { IsJoined: false }))
        {
            ServerFailureObserver.ThrowIfAny(failures);
            throw new InvalidOperationException(global::KeyLoad.UnitTests.Features.ClusterRouting.RequestCqrsHostedProtocol.CohortWorkNotJoined);
        }
        foreach (var node in nodes)
        {
            if (node.Runtime is { } runtime)
            {
                await ServerFailureObserver.ObserveAsync(runtime.StopAsync, failures);
            }

            await ServerFailureObserver.ObserveAsync(() => node.Journal.Consensus.StopAsync(deadline.Token), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => cluster.StopAllSilosAsync(deadline.Token), failures);
        try
        {
            await cluster.DisposeAsync();
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => node.DisposeAsync().AsTask(), failures);
        }

        ServerFailureObserver.ThrowIfAny(failures);
    }
}

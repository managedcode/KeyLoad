using KeyLoad.RecoveryTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed class AuthenticationExecutionTests
{
    [Test]
    [NotInParallel]
    public async Task RealHttpSdkAndOfficialMcpAuthenticateOncePerRequestThenFreshPolicyRefusalRepairReplayAndTwoColdCuts()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var collector = new AuthenticationExecutionCollector();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RequestCqrsHostedCluster.RunAsync(async hosts =>
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                try
                {
                    var source = hosts.Nodes[AuthenticationExecutionProtocol.NoEvents];
                    var state = await AuthenticationExecutionSeed.CreateAsync(source, token);
                    await AuthenticationExecutionHttpRefusal.RunAsync(source, state, collector, token);
                    await AuthenticationExecutionChanges.WarmAsync(source, state, collector, token);
                    await AuthenticationExecutionCold.RunAsync(hosts, state, collector, token);
                }
                catch (Exception original) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(original))
                {
                    hosts.RetainRoots();
                    throw;
                }
                catch (Exception original) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(original))
                {
                    hosts.RetainRoots();
                    throw;
                }
            }, failures);
        }, token, authenticationObservers: collector.ForOwner), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

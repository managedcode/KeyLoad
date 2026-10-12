using KeyLoad.CrashHost.Features.ClusterRouting;
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
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var hosts = await ControlledPartitionMovementNativeHosts.CreateWithAuthenticationObserverAsync(
                collector.ForOwner, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                try
                {
                    var state = await AuthenticationExecutionSeed.CreateAsync(hosts.Source, token);
                    await AuthenticationExecutionHttpRefusal.RunAsync(hosts.Source, state, collector, token);
                    await AuthenticationExecutionChanges.WarmAsync(hosts.Source, state, collector, token);
                    await AuthenticationExecutionCold.RunAsync(hosts, state, collector, token);
                }
                catch (Exception original) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(original))
                { hosts.RetainRoots(); throw; }
                catch (Exception original) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(original))
                { hosts.RetainRoots(); throw; }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

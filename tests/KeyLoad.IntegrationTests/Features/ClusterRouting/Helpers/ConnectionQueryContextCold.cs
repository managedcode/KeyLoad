using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ConnectionQueryContextCold
{
    internal static async Task<ConnectionQueryContextColdResult> RunAsync(RequestCqrsRf3Wave wave,
        RequestCqrsProbeFixture controls, ConnectionRf3OperationJoin operations, List<ConnectionRf3Caller> callers,
        RequestCqrsRf3Callers administrator, NodeEpochRf3Profile profile, ReplicaSiloDiscovery[] prior,
        ConnectionQueryContextColdEvidence evidence, long ordinal, CancellationToken token)
    {
        if (prior.Length != RequestCqrsRf3Protocol.NodeCount)
        {
            throw new InvalidOperationException("The original query cold owner scope is incomplete.");
        }
        await JoinAsync(controls, operations, callers, administrator, prior, token);
        var scenario = await evidence.ClaimAsync(profile.Incarnation, ordinal, token);
        await wave.RestartOriginalQueryOwnersAsync(scenario, token);
        await evidence.CaptureAsync(scenario, token);
        var current = await RequestCqrsRf3DiscoveryOracle.CaptureCurrentAsync(wave.App, profile, token);
        await RequestCqrsRf3DiscoveryOracle.AssertReplacementAsync(prior, current);
        var root = await RequestCqrsRf3Callers.ConnectAsync(wave.App, RequestCqrsRf3Protocol.Node1, profile.AdminKey, token);
        return new(root, current);
    }
    private static async Task JoinAsync(RequestCqrsProbeFixture controls, ConnectionRf3OperationJoin operations,
        List<ConnectionRf3Caller> callers, RequestCqrsRf3Callers administrator,
        ReplicaSiloDiscovery[] discovery, CancellationToken token)
    {
        var failures = new List<Exception>();
        await operations.JoinBoundedAsync(failures, token);
        await operations.JoinProducersAsync(controls, discovery, failures, token);
        foreach (var caller in callers)
        {
            await ServerFailureObserver.ObserveAsync(() => caller.DisposeAsync().AsTask(), failures);
        }
        await operations.JoinAfterTransportDisposedAsync(failures);
        await operations.JoinProducersAsync(controls, discovery, failures, token);
        await ServerFailureObserver.ObserveAsync(() => administrator.DisposeAsync().AsTask(), failures);
        if (operations.HasPending || !operations.ProducersDisposed(controls))
        {
            failures.Add(new InvalidOperationException("Original query callers and producer owners have not settled."));
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

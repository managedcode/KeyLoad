using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.IntegrationTests.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Messaging;

internal sealed class AtomicTriadStagedClusterCold
{
    private readonly AtomicTriadStagedClusterColdEvidence evidence = new();
    internal async Task RestartAsync(ClusterFixture fixture, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => evidence.RequireAsync(token), failures);
        var scenario = ModelSqlClusterColdOwnership.Require(fixture).Claim(fixture);
        foreach (var node in QueueProducerClusterProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillServerAsync(node, scenario, token), failures);
        }
        foreach (var node in QueueProducerClusterProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.BeginProcessRestartAsync(node, token), failures);
        }
        foreach (var node in QueueProducerClusterProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(node, token), failures);
        }
        foreach (var node in QueueProducerClusterProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node,
                WaitBehavior.WaitOnResourceUnavailable, token), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => evidence.CaptureAsync(scenario, token), failures);
        await ServerFailureObserver.ObserveAsync(() => evidence.RequireAsync(token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal Task RequireAsync(CancellationToken token) => evidence.RequireAsync(token);
}

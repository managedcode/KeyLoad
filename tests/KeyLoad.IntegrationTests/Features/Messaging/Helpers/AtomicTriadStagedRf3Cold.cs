using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class AtomicTriadStagedRf3Cold
{
    private readonly AtomicTriadStagedRf3ColdEvidence evidence = new();
    internal async Task RestartAsync(ClusterFixture fixture, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => evidence.RequireAsync(token), failures);
        var scenario = ModelSqlRf3ColdOwnership.Require(fixture).Claim(fixture);
        foreach (var node in QueueProducerRf3Protocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillContainerAsync(node, scenario, token), failures);
        }
        foreach (var node in QueueProducerRf3Protocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.BeginContainerRestartAsync(node, token), failures);
        }
        foreach (var node in QueueProducerRf3Protocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(node, token), failures);
        }
        foreach (var node in QueueProducerRf3Protocol.Nodes)
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

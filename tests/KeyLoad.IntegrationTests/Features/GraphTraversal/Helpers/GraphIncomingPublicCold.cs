using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphIncomingPublicCold
{
    private const int FirstNodeIndex = 0;
    private const string Scenario = "graph-incoming-current-policy-cold";
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];

    internal static async Task<NodeStatus[]> ObserveAsync(ClusterFixture fixture, CancellationToken token)
    {
        var statuses = new List<NodeStatus>();
        foreach (var node in Nodes)
        {
            using var http = McpCallerHttp.Create(fixture, node);
            var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
            statuses.Add(await McpCallerAssertions.SdkSuccessAsync(await administrator.StatusAsync(token)));
        }
        return [.. statuses];
    }

    internal static async Task RestartAsync(ClusterFixture fixture, NodeStatus[] before, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var node in Nodes)
        { await ServerFailureObserver.ObserveAsync(() => fixture.KillContainerAsync(node, Scenario, token), failures); }
        foreach (var node in Nodes)
        { await ServerFailureObserver.ObserveAsync(async () => await fixture.BeginContainerRestartAsync(node, token), failures); }
        foreach (var node in Nodes)
        { await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(node, token), failures); }
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.App.ResourceNotifications
            .WaitForResourceHealthyAsync(node, WaitBehavior.WaitOnResourceUnavailable, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        var after = await ObserveAsync(fixture, token);
        for (var index = FirstNodeIndex; index < Nodes.Length; index++)
        {
            await Assert.That(after[index].NodeId).IsEqualTo(before[index].NodeId);
            await Assert.That(after[index].Incarnation).IsEqualTo(before[index].Incarnation);
            await Assert.That(after[index].ReadGeneration).IsGreaterThanOrEqualTo(before[index].ReadGeneration);
            await Assert.That(after[index].Applied).IsGreaterThanOrEqualTo(before[index].Applied);
            await Assert.That(after[index].RoutingReady).IsTrue();
        }
    }
}

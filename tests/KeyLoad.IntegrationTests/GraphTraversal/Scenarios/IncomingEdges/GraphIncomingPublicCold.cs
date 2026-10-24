using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.GraphTraversal;

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
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillServerAsync(node, Scenario, token), failures);
        }
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.BeginProcessRestartAsync(node, token), failures);
        }
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(node, token), failures);
        }
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
            await Assert.That(after[index].Position).IsGreaterThanOrEqualTo(before[index].Position);
            await Assert.That(after[index].RoutingReady).IsTrue();
        }
    }
}

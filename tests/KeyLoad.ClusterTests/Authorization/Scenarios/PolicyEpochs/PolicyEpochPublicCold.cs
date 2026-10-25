using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.Authorization;

internal static class PolicyEpochPublicCold
{
    internal static async Task RunAsync(ClusterFixture fixture, PolicyEpochPublicState state, CancellationToken token)
    {
        var before = await CaptureAsync(fixture, state, token);
        await RestartAsync(fixture, token);
        var after = await CaptureAsync(fixture, state, token);
        for (var node = 0; node < before.Length; node++)
        {
            await Assert.That(after[node].Node.NodeId).IsEqualTo(before[node].Node.NodeId);
            await Assert.That(after[node].Node.Incarnation).IsEqualTo(before[node].Node.Incarnation);
            await Assert.That(after[node].Node.ReadGeneration).IsGreaterThanOrEqualTo(before[node].Node.ReadGeneration);
            await Assert.That(after[node].Node.Position).IsGreaterThanOrEqualTo(before[node].Node.Position);
            await Assert.That(after[node].PhysicalCut).IsGreaterThanOrEqualTo(before[node].PhysicalCut);
        }
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, fixture.AdminKey,
            root => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.CurrentReader.Secret,
                async reader =>
                {
                    await PolicyEpochPublicRefusals.AllAsync(fixture, reader, root.Sdk, state, ErrorCode.CursorExpired, token);
                    await PolicyEpochPublicAssertions.PagesAsync(reader, root.Sdk, state.Scenario, false, token);
                    await PolicyEpochPublicPolicy.ReplayAsync(root.Sdk, state, token);
                    return true;
                }, token), token);
    }

    private static async Task<(NodeStatus Node, long PhysicalCut)[]> CaptureAsync(ClusterFixture fixture,
        PolicyEpochPublicState state, CancellationToken token)
    {
        var cuts = new List<(NodeStatus Node, long PhysicalCut)>();
        foreach (var node in PolicyEpochPublicProtocol.Nodes)
        {
            using var http = McpCallerHttp.Create(fixture, node);
            var root = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
            var status = await McpCallerAssertions.SdkSuccessAsync(await root.StatusAsync(token));
            var image = await PolicyEpochPublicAssertions.ImageAsync(root, state.Scenario, token);
            cuts.Add((status, image.CutPosition));
        }
        return cuts.ToArray();
    }

    private static async Task RestartAsync(ClusterFixture fixture, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var node in PolicyEpochPublicProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillServerAsync(node, PolicyEpochPublicProtocol.ColdScenario, token), failures);
        }
        foreach (var node in PolicyEpochPublicProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.BeginProcessRestartAsync(node, token), failures);
        }
        foreach (var node in PolicyEpochPublicProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(node, token), failures);
        }
        foreach (var node in PolicyEpochPublicProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.App.ResourceNotifications
                .WaitForResourceHealthyAsync(node, WaitBehavior.WaitOnResourceUnavailable, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

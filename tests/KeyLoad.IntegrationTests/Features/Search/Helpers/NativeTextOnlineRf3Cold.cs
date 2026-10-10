using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextOnlineRf3Cold
{
    private const string Scenario = "online-text-cold-owner";
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
    internal static async Task RunAsync(ClusterFixture fixture, NativeTextMaintenanceRf3Scenario scenario,
        OnlineTextIndexMaintenanceRequest originalRequest, OnlineTextIndexMaintenanceResult original,
        OnlineTextIndexMaintenanceRequest currentRequest, OnlineTextIndexMaintenanceResult current,
        NodeStatus before, CancellationToken token)
    {
        var killed = await Task.WhenAll(Nodes.Select(node => ObserveAsync(() => fixture.KillContainerAsync(node, Scenario, token))));
        var restarted = await Task.WhenAll(Nodes.Select(node => ObserveAsync(() => fixture.RestartContainerAsync(node, token))));
        ServerFailureObserver.ThrowIfAny([.. killed.SelectMany(row => row), .. restarted.SelectMany(row => row)]);
        foreach (var node in Nodes)
        { await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node, WaitBehavior.WaitOnResourceUnavailable, token); }
        var failures = new List<Exception>();
        RequestCqrsRf3Callers? fresh = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fresh = await RequestCqrsRf3Callers.ConnectAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, token);
            var recovered = await McpCallerAssertions.SdkSuccessAsync(await fresh.Sdk.StatusAsync(token));
            await Assert.That(recovered.NodeId).IsEqualTo(before.NodeId);
            await Assert.That(recovered.Incarnation).IsEqualTo(before.Incarnation);
            await Assert.That(recovered.ReadGeneration).IsGreaterThanOrEqualTo(before.ReadGeneration);
            await Assert.That(recovered.Applied).IsGreaterThanOrEqualTo(before.Applied);
            await NativeTextOnlineRf3Assertions.ReplayAllAsync(fresh.Sdk, fresh.Mcp, currentRequest, current, token);
            await NativeTextOnlineRf3Assertions.LiteralAllAsync(fresh.Sdk, fresh.Mcp, scenario, true, token);
            await NativeTextOnlineRf3Assertions.ReplayAllAsync(fresh.Sdk, fresh.Mcp, originalRequest, original, token);
            await NativeTextOnlineRf3Assertions.LiteralAllAsync(fresh.Sdk, fresh.Mcp, scenario, true, token);
        }, failures);
        if (fresh is not null)
        { await ServerFailureObserver.ObserveAsync(() => fresh.DisposeAsync().AsTask(), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task<Exception[]> ObserveAsync(Func<Task> operation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        return [.. failures];
    }
}

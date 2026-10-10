using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextMaintenanceRf3Cold
{
    private const string Scenario = "explicit-text-maintenance-cold-owner";
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];

    internal static async Task RunAsync(ClusterFixture fixture, KeyLoadClient sdk, McpOfficialClient mcp,
        NativeTextMaintenanceRf3Scenario scenario, TextIndexMaintenanceRequest originalBuild,
        TextIndexMaintenanceRequest current, TextIndexMaintenanceResult result,
        NativeTextMaintenancePath path, CancellationToken token)
    {
        var retained = JsonDefaults.Serialize(result);
        var originalCut = await NativeTextMaintenanceRf3ReplayAssertions.CurrentAsync(
            sdk, mcp, scenario, current, retained, path, token);
        await NativeTextMaintenanceRf3ReplayAssertions.OldAsync(sdk, mcp, scenario, originalBuild, current, path, token);
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token));
        var stopped = await Task.WhenAll(Nodes.Select(node => ObserveAsync(() => fixture.KillContainerAsync(node, Scenario, token))));
        var restarted = await Task.WhenAll(Nodes.Select(node => ObserveAsync(() => fixture.RestartContainerAsync(node, token))));
        ServerFailureObserver.ThrowIfAny([.. stopped.SelectMany(row => row), .. restarted.SelectMany(row => row)]);
        foreach (var node in Nodes)
        { await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node, WaitBehavior.WaitOnResourceUnavailable, token); }
        var failures = new List<Exception>();
        RequestCqrsRf3Callers? fresh = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fresh = await RequestCqrsRf3Callers.ConnectAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, token);
            var after = await McpCallerAssertions.SdkSuccessAsync(await fresh.Sdk.StatusAsync(token));
            await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
            await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
            await Assert.That(after.ReadGeneration).IsGreaterThanOrEqualTo(before.ReadGeneration);
            await Assert.That(after.Applied).IsGreaterThanOrEqualTo(before.Applied);
            var recoveredCut = await NativeTextMaintenanceRf3ReplayAssertions.CurrentAsync(
                fresh.Sdk, fresh.Mcp, scenario, current, retained, path, token);
            await Assert.That(recoveredCut.Position).IsGreaterThanOrEqualTo(originalCut.Position);
            await Assert.That(recoveredCut.AppliedPosition).IsGreaterThanOrEqualTo(before.Applied);
            await Assert.That(recoveredCut.ReadGeneration).IsGreaterThanOrEqualTo(before.ReadGeneration);
            await NativeTextMaintenanceRf3ReplayAssertions.OldAsync(fresh.Sdk, fresh.Mcp, scenario, originalBuild, current, path, token);
            await NativeTextMaintenanceRf3ReplayAssertions.NoOpAsync(fresh.Sdk, fresh.Mcp, scenario, current, recoveredCut, result.IndexSha256!, path, token);
            await NativeTextMaintenanceRf3Release.RunAsync(fresh.Sdk, fresh.Mcp, scenario, current, path, token);
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

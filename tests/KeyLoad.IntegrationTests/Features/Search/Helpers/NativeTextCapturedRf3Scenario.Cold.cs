using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed partial class NativeTextCapturedRf3Scenario
{
    private async Task ColdAsync(NativeTextMaintenanceRf3Scenario seed,
        OnlineTextIndexMaintenanceRequest originalRequest, OnlineTextIndexMaintenanceResult original,
        OnlineTextIndexMaintenanceRequest currentRequest, OnlineTextIndexMaintenanceResult current,
        NodeStatus before, CancellationToken token)
    {
        var actual = wave ?? throw new InvalidOperationException();
        var nodes = Enumerable.Range(0, RequestCqrsRf3Protocol.NodeCount).Select(RequestCqrsRf3Protocol.NodeName).ToArray();
        var stopped = await Task.WhenAll(nodes.Select(node => ObserveAsync(() => actual.KillAsync(node, token))));
        var restarted = await Task.WhenAll(nodes.Select(node => ObserveAsync(() => actual.RestartAsync(node, token))));
        ServerFailureObserver.ThrowIfAny([.. stopped.SelectMany(row => row), .. restarted.SelectMany(row => row)]);
        foreach (var node in nodes)
        { await actual.App.ResourceNotifications.WaitForResourceHealthyAsync(node, WaitBehavior.WaitOnResourceUnavailable, token); }
        var errors = new List<Exception>();
        RequestCqrsRf3Callers? fresh = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fresh = await RequestCqrsRf3Callers.ConnectAsync(actual.App, publisherNode, publisherSecret, token);
            var after = await McpCallerAssertions.SdkSuccessAsync(await fresh.Sdk.StatusAsync(token));
            await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
            await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
            await Assert.That(after.ReadGeneration).IsGreaterThanOrEqualTo(before.ReadGeneration);
            await Assert.That(after.Applied).IsGreaterThanOrEqualTo(before.Applied);
            await NativeTextOnlineRf3Assertions.ReplayAllAsync(fresh.Sdk, fresh.Mcp, currentRequest, current, token);
            await NativeTextOnlineRf3Assertions.LiteralAllAsync(fresh.Sdk, fresh.Mcp, seed, true, token);
            await NativeTextOnlineRf3Assertions.ReplayAllAsync(fresh.Sdk, fresh.Mcp, originalRequest, original, token);
            await NativeTextOnlineRf3Assertions.LiteralAllAsync(fresh.Sdk, fresh.Mcp, seed, true, token);
        }, errors);
        if (fresh is not null)
        { await ServerFailureObserver.ObserveAsync(() => fresh.DisposeAsync().AsTask(), errors); }
        ServerFailureObserver.ThrowIfAny(errors);
    }

    private static async Task<Exception[]> ObserveAsync(Func<Task> action)
    {
        var errors = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(action, errors);
        return [.. errors];
    }
}

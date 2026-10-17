using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ChangeFeeds;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class AggregateReplayAuthorizationContinuation
{
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];

    internal static async Task RunAsync(ClusterFixture fixture, AggregateReplayRf3Scenario scenario,
        AggregateReplayRf3Tests owner, CancellationToken token)
    {
        var failures = new List<Exception>();
        AggregateReplayAuthorizationOriginal? original = null;
        AggregateReplayAuthorizationOriginal? fresh = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var callers = await ConnectAsync(fixture, scenario, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                original = await owner.VerifyPersistedRawGrantsAsync(scenario, callers.Sdk, callers.Mcp, token);
                await scenario.RevokeAsync(fixture, token);
                await owner.AssertRevokedPrincipalDeniedAsync(callers.Sdk, callers.Mcp,
                    original.Request, scenario.WorkerSecret, token);
                await AggregateReplayAuthorizationRoutes.RefusedAsync(callers, original, scenario.WorkerSecret, token);
                await scenario.RepairGrantAsync(fixture, token);
                await AggregateReplayAuthorizationRoutes.EpochDeniedAsync(callers, original, scenario.WorkerSecret, token);
                await AggregateReplayAuthorizationRoutes.HealthyPageAsync(callers, original, token);
                fresh = await AggregateReplayAuthorizationRoutes.FreshAsync(callers, original, token);
                await AggregateReplayAuthorizationRoutes.HealthyAsync(callers, fresh, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        var root = fixture.Root;
        var before = await StatusesAsync(fixture, token);
        await FeedLiveRf3Cold.RestartAsync(fixture, token);
        await Assert.That(fixture.Root).IsEqualTo(root);
        await Assert.That(Directory.Exists(root)).IsTrue();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var callers = await ConnectAsync(fixture, scenario, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await AggregateReplayAuthorizationRoutes.EpochDeniedAsync(callers, original!, scenario.WorkerSecret, token);
                await AggregateReplayAuthorizationRoutes.HealthyAsync(callers, fresh!, token);
                await IdentitiesAsync(before, await StatusesAsync(fixture, token));
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Task<RequestCqrsRf3Callers> ConnectAsync(ClusterFixture fixture,
        AggregateReplayRf3Scenario scenario, CancellationToken token)
        => RequestCqrsRf3Callers.ConnectObservedAsync(fixture.App, McpCallerProtocol.Node3,
            scenario.WorkerSecret, McpCallerHttp.Create(fixture, McpCallerProtocol.Node1), token);

    private static async Task<NodeStatus[]> StatusesAsync(ClusterFixture fixture, CancellationToken token)
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

    private static async Task IdentitiesAsync(NodeStatus[] before, NodeStatus[] after)
    {
        await Assert.That(after.Length).IsEqualTo(before.Length);
        for (var index = 0; index < before.Length; index++)
        {
            await Assert.That(after[index].NodeId).IsEqualTo(before[index].NodeId);
            await Assert.That(after[index].Incarnation).IsEqualTo(before[index].Incarnation);
            await Assert.That(after[index].ReadGeneration).IsGreaterThanOrEqualTo(before[index].ReadGeneration);
            await Assert.That(after[index].Applied).IsGreaterThanOrEqualTo(before[index].Applied);
            await Assert.That(after[index].Voters).IsEqualTo(before[index].Voters);
            await Assert.That(after[index].RoutingReady).IsTrue();
        }
    }
}

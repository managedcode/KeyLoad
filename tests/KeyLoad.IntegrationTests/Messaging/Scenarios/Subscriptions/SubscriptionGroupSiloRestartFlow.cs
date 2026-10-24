using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class SubscriptionGroupSiloRestartFlow
{
    private const string RestartReason = "subscription-checkpoint-silo-restart";

    internal static async Task RunAsync(ClusterFixture fixture, SubscriptionIndependentCheckpointR52State state,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var nodes = SubscriptionFilterClusterProtocol.Nodes;
        var selected = nodes[^1];
        var survivors = nodes.Where(node => node != selected).ToArray();
        var failures = new List<Exception>();
        var stopped = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            stopped = true;
            await fixture.KillServerAsync(selected, RestartReason, token);
            await WaitForSurvivorsAsync(fixture, survivors, token);
            await ReadCheckpointAsync(fixture, state, survivors[0], token);
            await fixture.RestartServerAsync(selected, token);
            stopped = false;
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(selected, token);
            await SubscriptionGroupSiloRestartCallers.RunAsync(fixture, state, survivors, token);
        }, failures);
        if (stopped)
        {
            using var cleanup = new CancellationTokenSource(DueRecurringClusterProtocol.ProgressWindow, TimeProvider.System);
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(selected, cleanup.Token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await SubscriptionIndependentCheckpointR52Callers.RunAsync(fixture, state, async (sdk, mcp, admin, official) =>
        {
            await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
            await SubscriptionIndependentCheckpointR52Replay.RunAsync(state, sdk, mcp, admin, official, token);
        }, token);
    }

    private static Task WaitForSurvivorsAsync(ClusterFixture fixture, string[] survivors, CancellationToken token)
        => ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
            foreach (var node in survivors)
            {
                using var http = McpCallerHttp.Create(fixture, node);
                var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
                var result = await sdk.StatusAsync(token);
                if (result.IsFailed || result.Value is not { RoutingReady: true } status ||
                    status.SiloCount != SubscriptionFilterClusterProtocol.Nodes.Length)
                {
                    return false;
                }
            }
            return true;
        }, token);

    private static async Task ReadCheckpointAsync(ClusterFixture fixture, SubscriptionIndependentCheckpointR52State state,
        string node, CancellationToken token)
    {
        var expected = SubscriptionIndependentCheckpointR52Current.Expected(state, state.Seed.Independent);
        using var http = McpCallerHttp.Create(fixture, node);
        var sdk = new KeyLoadClient(http, state.Seed.Manager.Secret, IntegrationClientOptions.Execution());
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.SubscriptionStatusAsync(state.Seed.Independent, token));
        await SubscriptionFilterClusterAssertions.EqualAsync(actual, expected);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, node, state.Seed.Manager.Secret, token);
        var official = (await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(await mcp.CallAsync(
            McpCallerTools.SubscriptionsStatus, new GetSubscriptionRequest(state.Seed.Independent), token))).Value;
        await SubscriptionFilterClusterAssertions.EqualAsync(official, expected);
    }
}

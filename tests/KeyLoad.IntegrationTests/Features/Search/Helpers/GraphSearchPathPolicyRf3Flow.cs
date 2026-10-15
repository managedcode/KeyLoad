using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.Server;
using P = KeyLoad.IntegrationTests.Features.Search.GraphSearchPathPolicyRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GraphSearchPathPolicyRf3Flow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            var retention = ClusterFixtureFailureRootRetention.For(fixture);
            retention.Enable();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                fixture.RegisterNativeCoverageCase<GraphSearchPathPolicyRf3Tests>(nameof(
                    GraphSearchPathPolicyRf3Tests.HiddenIntermediateStopsAllOperatorsUntilPersistedPolicyAndOwnerRepairSurviveReplayAndTwoCold));
                GraphSearchPathPolicyRf3State state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                        root => WarmAsync(fixture, root, deadline.Token), deadline.Token);
                }
                for (var cold = 0; cold < P.ColdCycles; cold++)
                {
                    using var deadline = McpCallerDeadline.Create();
                    await QueueProducerRf3Cold.RestartAsync(fixture, deadline.Token);
                    await WaveAsync(fixture, state, deadline.Token);
                }
            }, failures);
            if (failures.Count != 0)
            {
                retention.Retain();
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<GraphSearchPathPolicyRf3State> WarmAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers root, CancellationToken token)
    {
        var state = await GraphSearchPathPolicyRf3Setup.CreateAsync(fixture, root, token);
        return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async caller =>
        {
            await GraphSearchPathPolicyRf3Calls.RunAsync(root, caller, state, false, token);
            state = await GraphSearchPathPolicyRf3Setup.PolicyAsync(root, state, false, token);
            await GraphSearchPathPolicyRf3Calls.RunAsync(root, caller, state, true, token);
            state = await GraphSearchPathPolicyRf3Setup.PolicyAsync(root, state, true, token);
            await GraphSearchPathPolicyRf3Calls.RunAsync(root, caller, state, false, token);
            state = await GraphSearchPathPolicyRf3Setup.RepairOwnerAsync(root, state, token);
            await GraphSearchPathPolicyRf3Calls.RunAsync(root, caller, state, true, token);
            await GraphSearchPathPolicyRf3Calls.ReplayAsync(root, state, token);
            await GraphSearchPathPolicyRf3Calls.RunAsync(root, caller, state, true, token);
            return state;
        }, token);
    }

    private static Task<bool> WaveAsync(ClusterFixture fixture, GraphSearchPathPolicyRf3State state, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, state.Identity.Secret, async caller =>
            {
                await GraphSearchPathPolicyRf3Calls.RunAsync(root, caller, state, true, token);
                await GraphSearchPathPolicyRf3Calls.ReplayAsync(root, state, token);
                await GraphSearchPathPolicyRf3Calls.RunAsync(root, caller, state, true, token);
                return true;
            }, token), token);
}

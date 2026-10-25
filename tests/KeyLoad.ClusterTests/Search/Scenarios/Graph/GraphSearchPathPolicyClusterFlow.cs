using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.Messaging;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.StorageReplication;
using KeyLoad.Server;
using P = KeyLoad.ClusterTests.Search.GraphSearchPathPolicyClusterProtocol;

namespace KeyLoad.ClusterTests.Search;

internal static class GraphSearchPathPolicyClusterFlow
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
                GraphSearchPathPolicyClusterState state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                        root => WarmAsync(fixture, root, deadline.Token), deadline.Token);
                }
                for (var cold = 0; cold < P.ColdCycles; cold++)
                {
                    using var deadline = McpCallerDeadline.Create();
                    await QueueProducerClusterCold.RestartAsync(fixture, deadline.Token);
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

    private static async Task<GraphSearchPathPolicyClusterState> WarmAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers root, CancellationToken token)
    {
        var state = await GraphSearchPathPolicyClusterSetup.CreateAsync(fixture, root, token);
        return await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async caller =>
        {
            await GraphSearchPathPolicyClusterCalls.RunAsync(root, caller, state, false, token);
            state = await GraphSearchPathPolicyClusterSetup.PolicyAsync(root, state, false, token);
            await GraphSearchPathPolicyClusterCalls.RunAsync(root, caller, state, true, token);
            state = await GraphSearchPathPolicyClusterSetup.PolicyAsync(root, state, true, token);
            await GraphSearchPathPolicyClusterCalls.RunAsync(root, caller, state, false, token);
            state = await GraphSearchPathPolicyClusterSetup.RepairOwnerAsync(root, state, token);
            await GraphSearchPathPolicyClusterCalls.RunAsync(root, caller, state, true, token);
            await GraphSearchPathPolicyClusterCalls.ReplayAsync(root, state, token);
            await GraphSearchPathPolicyClusterCalls.RunAsync(root, caller, state, true, token);
            return state;
        }, token);
    }

    private static Task<bool> WaveAsync(ClusterFixture fixture, GraphSearchPathPolicyClusterState state, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, state.Identity.Secret, async caller =>
            {
                await GraphSearchPathPolicyClusterCalls.RunAsync(root, caller, state, true, token);
                await GraphSearchPathPolicyClusterCalls.ReplayAsync(root, state, token);
                await GraphSearchPathPolicyClusterCalls.RunAsync(root, caller, state, true, token);
                return true;
            }, token), token);
}

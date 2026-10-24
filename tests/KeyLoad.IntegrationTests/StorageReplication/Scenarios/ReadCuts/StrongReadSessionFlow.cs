using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.Server;
using static KeyLoad.IntegrationTests.StorageReplication.StrongReadSessionProtocol;

namespace KeyLoad.IntegrationTests.StorageReplication;

internal static class StrongReadSessionFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(() => BodyAsync(fixture), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task BodyAsync(ClusterFixture fixture)
    {
        await fixture.InitializeAsync();
        StrongReadSessionState state;
        using (var deadline = McpCallerDeadline.Create())
        {
            state = await RootAsync(fixture, McpCallerProtocol.Node1,
                root => StrongReadSessionSetup.RunAsync(fixture, root, deadline.Token), deadline.Token);
            await ReadWaveAsync(fixture, McpCallerProtocol.Node1, state, deadline.Token);
        }
        using (var deadline = McpCallerDeadline.Create())
        {
            state = await StrongReadSessionFailover.RunAsync(fixture, state,
                survivor => MutateWaveAsync(fixture, survivor, state, deadline.Token), deadline.Token);
            await ReadWaveAsync(fixture, McpCallerProtocol.Node1, state, deadline.Token);
        }
        var failures = new List<Exception>();
        for (var cold = 0; cold < ColdCuts; cold++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var deadline = McpCallerDeadline.Create();
                await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
                await ReadWaveAsync(fixture, McpCallerProtocol.Node1, state, deadline.Token);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static Task<StrongReadSessionState> MutateWaveAsync(ClusterFixture fixture, string node,
        StrongReadSessionState state, CancellationToken token)
        => RootAsync(fixture, node, root => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, node,
            state.Identity.Secret, async reader =>
            {
                await StrongReadSessionReads.HealthyAsync(root, reader, state, token);
                var created = await StrongReadSessionMutations.ApplyAsync(root, state, false, token);
                await StrongReadSessionReads.HealthyAsync(root, reader, created, token);
                var deleted = await StrongReadSessionMutations.ApplyAsync(root, created, true, token);
                await StrongReadSessionReads.HealthyAsync(root, reader, deleted, token);
                await StrongReadSessionReads.InvalidAsync(root, reader, deleted, false, token);
                await StrongReadSessionSetup.PolicyAsync(root, deleted, false, DeniedEpoch, token);
                await StrongReadSessionReads.InvalidAsync(root, reader, deleted, true, token);
                await StrongReadSessionSetup.PolicyAsync(root, deleted, true, RepairedEpoch, token);
                await StrongReadSessionReads.HealthyAsync(root, reader, deleted, token);
                await StrongReadSessionMutations.ReplayAsync(root, deleted, token);
                return deleted;
            }, token), token);
    private static Task<bool> ReadWaveAsync(ClusterFixture fixture, string node, StrongReadSessionState state,
        CancellationToken token)
        => RootAsync(fixture, node, root => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, node,
            state.Identity.Secret, async reader =>
            {
                await StrongReadSessionReads.HealthyAsync(root, reader, state, token);
                await StrongReadSessionReads.InvalidAsync(root, reader, state, false, token);
                await StrongReadSessionMutations.ReplayAsync(root, state, token);
                return true;
            }, token), token);
    private static Task<T> RootAsync<T>(ClusterFixture fixture, string node, Func<RequestCqrsClusterCallers, Task<T>> operation,
        CancellationToken token) => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, node, fixture.AdminKey, operation, token);
}

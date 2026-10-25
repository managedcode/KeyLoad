using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.Server;
using static KeyLoad.ClusterTests.QueryExecution.QueryEquivalentIntegerClusterDecimalProtocol;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class QueryEquivalentIntegerClusterFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                QueryEquivalentIntegerClusterState state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await RootAsync(fixture, root => QueryEquivalentIntegerClusterSetup.RunAsync(fixture, root, deadline.Token), deadline.Token);
                }
                using (var deadline = McpCallerDeadline.Create())
                {
                    await WaveAsync(fixture, state, true, deadline.Token);
                }
                for (var cold = 0; cold < ColdCuts; cold++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
                        await WaveAsync(fixture, state, false, deadline.Token);
                    }, failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static Task<bool> WaveAsync(ClusterFixture fixture, QueryEquivalentIntegerClusterState state,
        bool warm, CancellationToken token)
        => RootAsync(fixture, root => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Identity.Secret, async reader =>
            {
                await QueryEquivalentIntegerClusterCalls.MatrixAsync(root, reader, state, false, token);
                if (warm)
                {
                    await QueryEquivalentIntegerClusterSetup.PolicyAsync(root, state, DeniedEpoch, false, token);
                    await QueryEquivalentIntegerClusterCalls.MatrixAsync(root, reader, state, true, token);
                    await QueryEquivalentIntegerClusterSetup.PolicyAsync(root, state, RepairEpoch, true, token);
                    await QueryEquivalentIntegerClusterCalls.MatrixAsync(root, reader, state, false, token);
                }
                await QueryEquivalentIntegerClusterCurrent.ReplayAsync(root, state, token);
                return true;
            }, token), token);
    private static Task<T> RootAsync<T>(ClusterFixture fixture, Func<RequestCqrsClusterCallers, Task<T>> operation,
        CancellationToken token) => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, operation, token);
}

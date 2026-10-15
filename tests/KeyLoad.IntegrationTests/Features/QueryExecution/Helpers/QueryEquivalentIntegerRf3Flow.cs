using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using static KeyLoad.IntegrationTests.Features.QueryExecution.QueryEquivalentIntegerRf3DecimalProtocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryEquivalentIntegerRf3Flow
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
                QueryEquivalentIntegerRf3State state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await RootAsync(fixture, root => QueryEquivalentIntegerRf3Setup.RunAsync(fixture, root, deadline.Token), deadline.Token);
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
                        await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token);
                        await WaveAsync(fixture, state, false, deadline.Token);
                    }, failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static Task<bool> WaveAsync(ClusterFixture fixture, QueryEquivalentIntegerRf3State state,
        bool warm, CancellationToken token)
        => RootAsync(fixture, root => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Identity.Secret, async reader =>
            {
                await QueryEquivalentIntegerRf3Calls.MatrixAsync(root, reader, state, false, token);
                if (warm)
                {
                    await QueryEquivalentIntegerRf3Setup.PolicyAsync(root, state, DeniedEpoch, false, token);
                    await QueryEquivalentIntegerRf3Calls.MatrixAsync(root, reader, state, true, token);
                    await QueryEquivalentIntegerRf3Setup.PolicyAsync(root, state, RepairEpoch, true, token);
                    await QueryEquivalentIntegerRf3Calls.MatrixAsync(root, reader, state, false, token);
                }
                await QueryEquivalentIntegerRf3Current.ReplayAsync(root, state, token);
                return true;
            }, token), token);
    private static Task<T> RootAsync<T>(ClusterFixture fixture, Func<RequestCqrsRf3Callers, Task<T>> operation,
        CancellationToken token) => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, operation, token);
}

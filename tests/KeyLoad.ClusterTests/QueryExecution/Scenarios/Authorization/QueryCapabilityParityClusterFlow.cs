using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class QueryCapabilityParityClusterFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new QueryExecutionOptions());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                QueryCapabilityParityClusterState state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                        root => QueryCapabilityParityClusterSetup.RunAsync(fixture, root, deadline.Token), deadline.Token);
                }
                await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, state, true), failures);
                for (var cold = 0; cold < 2; cold++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
                        await WaveAsync(fixture, state, false);
                    }, failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task WaveAsync(ClusterFixture fixture, QueryCapabilityParityClusterState state, bool warm)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret, async reader =>
            {
                await ReadAndRepairAsync(root, reader, state, warm, deadline.Token);
                return true;
            }, deadline.Token), deadline.Token);
    }
    private static async Task ReadAndRepairAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        QueryCapabilityParityClusterState state, bool warm, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => QueryCapabilityParityClusterCurrent.ObserveAsync(root, state, async () =>
        {
            await QueryCapabilityParityClusterRefusals.RunAsync(reader, state, false, token);
            await QueryCapabilityParityClusterPages.RunAsync(reader, state, token);
        }, token), failures);
        if (warm)
        {
            await ServerFailureObserver.ObserveAsync(() => QueryCapabilityParityClusterSetup.PolicyAsync(root, state, 2L, false, token), failures);
            await ServerFailureObserver.ObserveAsync(() => QueryCapabilityParityClusterCurrent.ObserveAsync(root, state,
                () => QueryCapabilityParityClusterRefusals.RunAsync(reader, state, true, token), token), failures);
            await ServerFailureObserver.ObserveAsync(() => QueryCapabilityParityClusterSetup.PolicyAsync(root, state, 3L, true, token), failures);
            await ServerFailureObserver.ObserveAsync(() => QueryCapabilityParityClusterCurrent.ObserveAsync(root, state,
                () => QueryCapabilityParityClusterPages.RunAsync(reader, state, token), token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

}

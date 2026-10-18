using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ModelHeadClusterFlow
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
                ModelHeadClusterState state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                        root => ModelHeadClusterSetup.RunAsync(fixture, root, deadline.Token), deadline.Token);
                }
                await WarmAsync(fixture, state);
                var coldFailures = new List<Exception>();
                for (var cycle = 0; cycle < 2; cycle++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
                        await WaveAsync(fixture, state, deadline.Token);
                    }, coldFailures);
                }
                ServerFailureObserver.ThrowIfAny(coldFailures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task WarmAsync(ClusterFixture fixture, ModelHeadClusterState state)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async reader =>
            {
                await ModelHeadClusterOperations.RunAsync(root, reader, state, true, deadline.Token);
                await ModelHeadClusterSetup.PolicyAsync(root, state, true, 3L, deadline.Token);
                await ModelHeadClusterOperations.RunAsync(root, reader, state, false, deadline.Token);
                await ModelHeadClusterSetup.PolicyAsync(root, state, false, 4L, deadline.Token);
                await ModelHeadClusterOperations.RunAsync(root, reader, state, true, deadline.Token);
                await ModelHeadClusterSetup.PolicyAsync(root, state, true, 5L, deadline.Token);
                await ModelHeadClusterOperations.RunAsync(root, reader, state, false, deadline.Token);
                return true;
            }, deadline.Token), deadline.Token);
    }
    private static Task<bool> WaveAsync(ClusterFixture fixture, ModelHeadClusterState state, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async reader =>
            {
                await ModelHeadClusterOperations.RunAsync(root, reader, state, false, token);
                return true;
            }, token), token);
}

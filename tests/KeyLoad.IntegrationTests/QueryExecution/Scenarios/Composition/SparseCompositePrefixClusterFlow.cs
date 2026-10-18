using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SparseCompositePrefixClusterFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, bool includeNull, bool includeMissing, CancellationToken token)
    {
        var state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            root => SparseCompositePrefixClusterSetup.RunAsync(fixture, root, includeNull, includeMissing, token), token);
        await WaveAsync(fixture, state, false, token);
        await PolicyAsync(fixture, state, false, 3L, token);
        await WaveAsync(fixture, state, true, token);
        await PolicyAsync(fixture, state, true, 4L, token);
        await WaveAsync(fixture, state, false, token);
        var failures = new List<Exception>();
        for (var cold = 0; cold < 2; cold++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await ModelSqlClusterCold.RunAsync(fixture, token);
                await WaveAsync(fixture, state, false, token);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static Task<bool> PolicyAsync(ClusterFixture fixture, SparseCompositePrefixClusterState state, bool use, long epoch, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            await SparseCompositePrefixClusterSetup.PolicyAsync(root, state, use, epoch, token);
            return true;
        }, token);
    private static Task<bool> WaveAsync(ClusterFixture fixture, SparseCompositePrefixClusterState state, bool denied, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async reader =>
            {
                await SparseCompositePrefixClusterOperations.RunAsync(root, reader, state, denied, token);
                return true;
            }, token), token);
}

using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class DocumentLiveProfileClusterFlow
{
    internal static async Task RunAsync(ModelQuerySourceKind kind)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                DocumentLiveProfileClusterState? state = null;
                using (var deadline = McpCallerDeadline.Create())
                {
                    await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
                    {
                        state = await DocumentLiveProfileClusterSetup.RunAsync(root, deadline.Token);
                        return true;
                    }, deadline.Token);
                }
                await WaveAsync(fixture, state!, kind, true);
                for (var cycle = 0; cycle < 2; cycle++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
                        await WaveAsync(fixture, state!, kind, false);
                    }, failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task WaveAsync(ClusterFixture fixture, DocumentLiveProfileClusterState state, ModelQuerySourceKind kind, bool warm)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, fixture.AdminKey, async root =>
        {
            await DocumentLiveProfileClusterOwner.ObserveAsync(root, state, async () =>
            {
                await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Key, async reader =>
                {
                    if (warm)
                    {
                        await DocumentLiveProfileClusterWave.WarmAsync(root, reader, state, kind, deadline.Token);
                    }
                    else
                    {
                        await DocumentLiveProfileClusterWave.FinalAsync(root, reader, state, kind, deadline.Token);
                    }
                    return true;
                }, deadline.Token);
            }, deadline.Token);
            return true;
        }, deadline.Token);
    }
}

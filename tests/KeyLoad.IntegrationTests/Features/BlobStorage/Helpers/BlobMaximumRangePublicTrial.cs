using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobMaximumRangePublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async callers =>
            {
                var publication = await BlobMaximumRangePublicPublication.CreateAsync(callers, token);
                await BlobMaximumRangePublicReplay.RequireAsync(callers, publication, token);
                return publication;
            }, token);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await ColdAsync(fixture, token);
            _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                fixture.AdminKey, async callers =>
                {
                    await BlobMaximumRangePublicReplay.RequireAsync(callers, state, token);
                    return true;
                }, token);
        }
    }

    private static async Task ColdAsync(ClusterFixture fixture, CancellationToken token)
    {
        string[] nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
        var failures = new List<Exception>();
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillContainerAsync(node, "blob-maximum-range-public", token), failures);
        }
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.BeginContainerRestartAsync(node, token), failures);
        }
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(node, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

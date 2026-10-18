using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

using KeyLoad.IntegrationTests.Features.Search;
internal static class BlobScopedBytesR42Trial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var states = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async callers =>
        {
            var states = await BlobScopedBytesR42Seed.CreateAsync(callers, token);
            foreach (var state in states.Take(4))
            {
                await BlobScopedBytesR42Publication.WriteAsync(callers, state, states, token);
            }
            await BlobScopedBytesR42Cleanup.RunAsync(callers, states, token);
            foreach (var state in states.Skip(1).Take(3))
            {
                await BlobScopedBytesR42Publication.CompleteAsync(callers, state, states, token);
            }
            await BlobScopedBytesR42Replay.RunAsync(callers, states, token);
            return states;
        }, token);
        for (var cold = 0; cold < 2; cold++)
        {
            await NativeTextProtectedPublicColdRestart.RunAsync(fixture, token);
            _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async callers =>
            {
                await BlobScopedBytesR42Assertions.CurrentAsync(callers, states, token);
                await BlobScopedBytesR42Replay.RunAsync(callers, states, token);
                return true;
            }, token);
        }
    }
}

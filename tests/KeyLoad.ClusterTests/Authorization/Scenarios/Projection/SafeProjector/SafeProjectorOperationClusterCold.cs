using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Authorization;

internal static class SafeProjectorOperationClusterCold
{
    internal static async Task RunAsync(ClusterFixture fixture, SafeProjectorOperationClusterHistory history, CancellationToken token)
    {
        var baseline = await ObserveAsync(fixture, history, token);
        for (var cold = SafeProjectorOperationClusterProtocol.FirstColdCycle;
            cold < SafeProjectorOperationClusterProtocol.ColdCycles; cold++)
        {
            await ModelSqlClusterCold.RunAsync(fixture, token);
            var current = await ObserveAsync(fixture, history, token);
            await Assert.That(StringComparer.Ordinal.Equals(current, baseline)).IsTrue();
        }
    }

    private static async Task<string> ObserveAsync(ClusterFixture fixture,
        SafeProjectorOperationClusterHistory history, CancellationToken token)
    {
        return await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            var image = await SafeProjectorOperationClusterImages.CaptureAsync(root, history, token);
            await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, history.State.Key, async reader =>
            {
                await SafeProjectorOperationClusterReplay.AllAsync(root, reader, history, SafeProjectorOperationClusterProtocol.FinalMaskedEpoch, token);
                await SafeProjectorOperationClusterContinuation.FinalAsync(root, reader, history, token);
                var feed = await SafeProjectorOperationClusterChanges.WaveAsync(root, reader, history, false, token);
                await SafeProjectorOperationClusterCursor.CaptureAsync(root, reader, history, false, feed.Cursor, token);
                return true;
            }, token);
            await Assert.That(StringComparer.Ordinal.Equals(await SafeProjectorOperationClusterImages.CaptureAsync(root, history, token), image)).IsTrue();
            return image;
        }, token);
    }
}

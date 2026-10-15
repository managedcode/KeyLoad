using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationRf3Cold
{
    internal static async Task RunAsync(ClusterFixture fixture, SafeProjectorOperationRf3History history, CancellationToken token)
    {
        var baseline = await ObserveAsync(fixture, history, token);
        for (var cold = SafeProjectorOperationRf3Protocol.FirstColdCycle;
            cold < SafeProjectorOperationRf3Protocol.ColdCycles; cold++)
        {
            await ModelSqlRf3Cold.RunAsync(fixture, token);
            var current = await ObserveAsync(fixture, history, token);
            await Assert.That(StringComparer.Ordinal.Equals(current.Image, baseline.Image)).IsTrue();
            await SqlRf3Protocol.EqualAsync(baseline.Placement, current.Placement);
        }
    }

    private static async Task<(string Image, AtomicPartitionPlacementResolution Placement)> ObserveAsync(ClusterFixture fixture,
        SafeProjectorOperationRf3History history, CancellationToken token)
    {
        return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(
                new(SafeProjectorOperationRf3Protocol.PlacementReadVersion, history.State.Partition), token));
            var image = await SafeProjectorOperationRf3Images.CaptureAsync(root, history, token);
            await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, history.State.Key, async reader =>
            {
                await SafeProjectorOperationRf3Replay.AllAsync(root, reader, history, SafeProjectorOperationRf3Protocol.FinalMaskedEpoch, token);
                await SafeProjectorOperationRf3Continuation.FinalAsync(root, reader, history, token);
                var feed = await SafeProjectorOperationRf3Changes.WaveAsync(root, reader, history, false, token);
                await SafeProjectorOperationRf3Cursor.CaptureAsync(root, reader, history, false, feed.Cursor, token);
                return true;
            }, token);
            await Assert.That(StringComparer.Ordinal.Equals(await SafeProjectorOperationRf3Images.CaptureAsync(root, history, token), image)).IsTrue();
            return (Image: image, Placement: placement);
        }, token);
    }
}

using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using P = KeyLoad.IntegrationTests.Features.Authorization.SchemaVisibleProjectionRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SchemaVisibleProjectionRf3Cold
{
    internal static async Task RunAsync(ClusterFixture fixture, SafeProjectorOperationRf3History history, CancellationToken token)
    {
        var baseline = await ObserveAsync(fixture, history, token);
        var failures = new List<Exception>();
        for (var cold = P.First; cold < P.ColdCuts; cold++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await ModelSqlRf3Cold.RunAsync(fixture, token);
                var current = await ObserveAsync(fixture, history, token);
                await Assert.That(StringComparer.Ordinal.Equals(baseline.Image, current.Image)).IsTrue();
                await SqlRf3Protocol.EqualAsync(baseline.Placement, current.Placement);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Task<(string Image, AtomicPartitionPlacementResolution Placement)> ObserveAsync(ClusterFixture fixture,
        SafeProjectorOperationRf3History history, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(
                new(SafeProjectorOperationRf3Protocol.PlacementReadVersion, history.State.Partition), token));
            var image = await SafeProjectorOperationRf3Images.CaptureAsync(root, history, token);
            await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, history.State.Key, async reader =>
            {
                var failures = new List<Exception>();
                await ServerFailureObserver.ObserveAsync(() => SchemaVisibleProjectionRf3Root.WaveAsync(root, reader, history, token), failures);
                await ServerFailureObserver.ObserveAsync(() => SchemaVisibleProjectionRf3Replay.AllAsync(root, reader, history, P.RootEpoch, token), failures);
                ServerFailureObserver.ThrowIfAny(failures);
                return true;
            }, token);
            await Assert.That(StringComparer.Ordinal.Equals(image,
                await SafeProjectorOperationRf3Images.CaptureAsync(root, history, token))).IsTrue();
            return (Image: image, Placement: placement);
        }, token);
}

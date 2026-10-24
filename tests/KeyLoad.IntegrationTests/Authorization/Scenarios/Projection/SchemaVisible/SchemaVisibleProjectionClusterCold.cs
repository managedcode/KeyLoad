using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.Server;
using P = KeyLoad.IntegrationTests.Authorization.SchemaVisibleProjectionClusterProtocol;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class SchemaVisibleProjectionClusterCold
{
    internal static async Task RunAsync(ClusterFixture fixture, SafeProjectorOperationClusterHistory history, CancellationToken token)
    {
        var baseline = await ObserveAsync(fixture, history, token);
        var failures = new List<Exception>();
        for (var cold = P.First; cold < P.ColdCuts; cold++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await ModelSqlClusterCold.RunAsync(fixture, token);
                var current = await ObserveAsync(fixture, history, token);
                await Assert.That(StringComparer.Ordinal.Equals(baseline, current)).IsTrue();
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Task<string> ObserveAsync(ClusterFixture fixture,
        SafeProjectorOperationClusterHistory history, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            var image = await SafeProjectorOperationClusterImages.CaptureAsync(root, history, token);
            await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, history.State.Key, async reader =>
            {
                var failures = new List<Exception>();
                await ServerFailureObserver.ObserveAsync(() => SchemaVisibleProjectionClusterRoot.WaveAsync(root, reader, history, token), failures);
                await ServerFailureObserver.ObserveAsync(() => SchemaVisibleProjectionClusterReplay.AllAsync(root, reader, history, P.RootEpoch, token), failures);
                ServerFailureObserver.ThrowIfAny(failures);
                return true;
            }, token);
            await Assert.That(StringComparer.Ordinal.Equals(image,
                await SafeProjectorOperationClusterImages.CaptureAsync(root, history, token))).IsTrue();
            return image;
        }, token);
}

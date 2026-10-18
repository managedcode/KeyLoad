using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobUnderfilledR47Trial
{
    private const string ColdReason = "blob-part-identity-r40";

    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async callers =>
            {
                var original = await BlobPartIdentityR40Operations.StartAsync(callers, token);
                await BlobPartIdentityR40Replay.RunAsync(callers, original, token);
                var owned = new BlobUnderfilledR47State(original);
                await BlobTailShapeR48Refusals.BothAsync(callers, owned.TailShape, token);
                await BlobUnderfilledR47Refusals.BothAsync(callers, owned, token);
                return owned;
            }, token);
        await ColdAsync(fixture, token);
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, fixture.AdminKey,
            async callers =>
            {
                await BlobPartIdentityR40Assertions.CurrentAsync(callers, state.Original, token);
                await BlobPartIdentityR40Replay.RunAsync(callers, state.Original, token);
                await BlobTailShapeR48Refusals.BothAsync(callers, state.TailShape, token);
                await BlobUnderfilledR47Refusals.BothAsync(callers, state, token);
                await BlobUnderfilledR47Publication.FinishAsync(callers, state, token);
                await BlobPartIdentityR40Replay.RunAsync(callers, state.Original, token);
                await BlobTailShapeR48Refusals.BothAsync(callers, state.TailShape, token);
                await BlobUnderfilledR47Refusals.BothAsync(callers, state, token);
                return true;
            }, token);
        await ColdAsync(fixture, token);
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, fixture.AdminKey,
            async callers =>
            {
                await BlobPartIdentityR40Assertions.CurrentAsync(callers, state.Original, token);
                await BlobPartIdentityR40Replay.RunAsync(callers, state.Original, token);
                await BlobTailShapeR48Refusals.BothAsync(callers, state.TailShape, token);
                await BlobUnderfilledR47Refusals.BothAsync(callers, state, token);
                return true;
            }, token);
    }

    private static async Task ColdAsync(ClusterFixture fixture, CancellationToken token)
    {
        string[] nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
        var failures = new List<Exception>();
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillServerAsync(node, ColdReason, token), failures);
        }
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.BeginProcessRestartAsync(node, token), failures);
        }
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(node, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

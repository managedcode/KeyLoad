using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPartIdentityR40Trial
{
    private const string ColdReason = "blob-part-identity-r40";

    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async callers =>
            {
                var current = await BlobPartIdentityR40Operations.StartAsync(callers, token);
                await BlobPartIdentityR40Replay.RunAsync(callers, current, token);
                return current;
            }, token);
        await ColdAsync(fixture, token);
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, fixture.AdminKey,
            async callers =>
            {
                await BlobPartIdentityR40Assertions.CurrentAsync(callers, state, token);
                await BlobPartIdentityR40Replay.RunAsync(callers, state, token);
                await BlobPartIdentityR40Operations.FinishAsync(callers, state, token);
                await BlobPartIdentityR40Replay.RunAsync(callers, state, token);
                return true;
            }, token);
        await ColdAsync(fixture, token);
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, fixture.AdminKey,
            async callers =>
            {
                await BlobPartIdentityR40Assertions.CurrentAsync(callers, state, token);
                await BlobPartIdentityR40Replay.RunAsync(callers, state, token);
                return true;
            }, token);
    }

    private static async Task ColdAsync(ClusterFixture fixture, CancellationToken token)
    {
        string[] nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
        var failures = new List<Exception>();
        foreach (var node in nodes)
        { await ServerFailureObserver.ObserveAsync(() => fixture.KillContainerAsync(node, ColdReason, token), failures); }
        foreach (var node in nodes)
        { await ServerFailureObserver.ObserveAsync(async () => await fixture.BeginContainerRestartAsync(node, token), failures); }
        foreach (var node in nodes)
        { await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(node, token), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.Server;
using static KeyLoad.IntegrationTests.Features.StorageReplication.StrongReadSessionProtocol;

namespace KeyLoad.IntegrationTests.Features.StorageReplication;

internal static class StrongReadSessionFailover
{
    internal static async Task<StrongReadSessionState> RunAsync(ClusterFixture fixture, StrongReadSessionState state,
        Func<string, Task<StrongReadSessionState>> operation, CancellationToken token)
    {
        string[] members = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
        var selected = members[^1];
        var survivor = members[0];
        StrongReadSessionState? result = null;
        var stopped = false;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            stopped = true;
            await fixture.KillServerAsync(selected, KillScenario, token);
            await ReadWhileUnavailableAsync(fixture, survivor, state, token);
            await fixture.RestartServerAsync(selected, token);
            stopped = false;
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(selected, token);
            result = await operation(survivor);
        }, failures);
        using var cleanup = new CancellationTokenSource(RequestCqrsClusterProtocol.CleanupDeadline, TimeProvider.System);
        if (stopped)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(selected, cleanup.Token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(MissingState);
    }
    private static async Task ReadWhileUnavailableAsync(ClusterFixture fixture, string survivor, StrongReadSessionState state,
        CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var http = McpCallerHttp.Create(fixture, survivor);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
                var original = state.Commits.First();
                var replay = await sdk.CommitAsync(original.Command, token);
                await Assert.That(replay.IsFailed).IsTrue();
                await Assert.That(replay.Value).IsNull();
                await Assert.That(replay.Problem?.ErrorCode is nameof(ErrorCode.OwnershipLost)
                    or nameof(ErrorCode.UnknownWriteOutcome)).IsTrue();
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, survivor, fixture.AdminKey, async root =>
        {
            await StrongReadSessionCurrent.CheckAsync(root, state, token);
            return true;
        }, token);
    }
}

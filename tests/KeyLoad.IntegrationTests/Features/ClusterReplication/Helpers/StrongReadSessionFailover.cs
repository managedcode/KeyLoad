using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.StrongReadSessionProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class StrongReadSessionFailover
{
    internal static async Task<StrongReadSessionState> RunAsync(ClusterFixture fixture, StrongReadSessionState state,
        Func<string, Task<StrongReadSessionState>> operation, CancellationToken token)
    {
        var leader = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async root => new Uri((await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.StatusAsync(token))).Leader!).Host, token);
        string[] members = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
        if (!members.Contains(leader, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(MissingLeader);
        }
        var survivor = members.First(member => member != leader);
        StrongReadSessionState? result = null;
        var stopped = false;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            stopped = true;
            await fixture.KillContainerAsync(leader, KillScenario, token);
            await ElectionAsync(fixture, survivor, state, token);
            result = await operation(survivor);
        }, failures);
        using var cleanup = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System);
        if (stopped)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(leader, cleanup.Token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(MissingState);
    }
    private static async Task ElectionAsync(ClusterFixture fixture, string survivor, StrongReadSessionState state,
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
                var replay = await ClusterReplicationTestSupport.RetryDuringElectionAsync(
                    () => sdk.CommitAsync(original.Command, token), token);
                await SqlRf3Protocol.EqualAsync(original.Receipt, await McpCallerAssertions.SdkSuccessAsync(replay));
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, survivor, fixture.AdminKey, async root =>
        {
            await StrongReadSessionCurrent.CheckAsync(root, state, token);
            return true;
        }, token);
    }
}

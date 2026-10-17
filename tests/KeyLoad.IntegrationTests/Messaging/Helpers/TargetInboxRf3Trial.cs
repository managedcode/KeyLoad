using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class TargetInboxRf3Trial
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
                var administrator = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
                var seed = await TargetInboxRf3Seed.CreateAsync(fixture, administrator, deadline.Token);
                using var workerHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
                var worker = new KeyLoadClient(workerHttp, seed.Identity.Secret, IntegrationClientOptions.Execution());
                var state = await TargetInboxRf3Original.ExecuteAsync(fixture, worker, seed.Source, seed.Target, seed.Identity, deadline.Token);
                await TargetInboxRf3Cold.RestartAsync(fixture, deadline.Token);
                await WithMcpAsync(fixture, state, async mcp =>
                {
                    await TargetInboxRf3Replay.ExecuteAsync(worker, mcp, state, true, deadline.Token);
                    await TargetInboxRf3Policy.ExecuteAsync(fixture, worker, mcp, state, deadline.Token);
                }, deadline.Token);
                await TargetInboxRf3Cold.RestartAsync(fixture, deadline.Token);
                await WithMcpAsync(fixture, state, mcp => TargetInboxRf3Finish.ExecuteAsync(worker, mcp, state, deadline.Token), deadline.Token);
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task WithMcpAsync(ClusterFixture fixture, TargetInboxRf3State state,
        Func<McpOfficialClient, Task> execute, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, state.Identity.Secret, token);
            await ServerFailureObserver.ObserveAsync(() => execute(mcp), failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

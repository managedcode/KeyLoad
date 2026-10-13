using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionPhasePublicTrial
{
    internal static async Task RunAsync(Func<ClusterFixture, SubscriptionFilterRf3Seed, CancellationToken, Task> flow)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                var seed = await SubscriptionFilterRf3Setup.CreateAsync(fixture, deadline.Token);
                await flow(fixture, seed, deadline.Token);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task WithCallersAsync(ClusterFixture fixture, SubscriptionFilterRf3Seed seed,
        Func<KeyLoadClient, McpOfficialClient, Task> flow, CancellationToken token)
    {
        var failures = new List<Exception>();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, seed.Manager.Secret, IntegrationClientOptions.Execution());
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Manager.Secret, token);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => flow(sdk, mcp), failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}

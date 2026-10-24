using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class SubscriptionIndependentCheckpointR52Callers
{
    internal static async Task RunAsync(ClusterFixture fixture, SubscriptionIndependentCheckpointR52State state,
        Func<KeyLoadClient, McpOfficialClient, KeyLoadClient, McpOfficialClient, Task> action, CancellationToken token)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                () => AdministratorAsync(fixture, state, http, action, failures, token), failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task AdministratorAsync(ClusterFixture fixture, SubscriptionIndependentCheckpointR52State state,
        HttpClient http, Func<KeyLoadClient, McpOfficialClient, KeyLoadClient, McpOfficialClient, Task> action,
        List<Exception> failures, CancellationToken token)
    {
        var admin = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await using var official = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, token);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
            () => ManagerAsync(fixture, state, admin, official, action, failures, token), failures).ConfigureAwait(false);
    }

    private static async Task ManagerAsync(ClusterFixture fixture, SubscriptionIndependentCheckpointR52State state,
        KeyLoadClient admin, McpOfficialClient official,
        Func<KeyLoadClient, McpOfficialClient, KeyLoadClient, McpOfficialClient, Task> action,
        List<Exception> failures, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            var sdk = new KeyLoadClient(http, state.Seed.Manager.Secret, IntegrationClientOptions.Execution());
            await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, state.Seed.Manager.Secret, token);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                () => action(sdk, mcp, admin, official), failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
    }
}

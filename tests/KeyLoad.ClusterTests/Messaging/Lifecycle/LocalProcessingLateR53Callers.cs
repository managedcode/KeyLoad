using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class LocalProcessingLateR53Callers
{
    internal static async Task RunAsync(ClusterFixture fixture, LocalProcessingLateR53State state,
        Func<KeyLoadClient, McpOfficialClient, KeyLoadClient, McpOfficialClient, Task> action, CancellationToken token)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                () => AdministratorAsync(fixture, state, http, action, failures, token), failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task AdministratorAsync(ClusterFixture fixture, LocalProcessingLateR53State state,
        HttpClient http, Func<KeyLoadClient, McpOfficialClient, KeyLoadClient, McpOfficialClient, Task> action,
        List<Exception> failures, CancellationToken token)
    {
        var admin = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await using var official = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, token);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
            () => WorkerAsync(fixture, state, admin, official, action, failures, token), failures);
    }

    private static async Task WorkerAsync(ClusterFixture fixture, LocalProcessingLateR53State state,
        KeyLoadClient admin, McpOfficialClient official,
        Func<KeyLoadClient, McpOfficialClient, KeyLoadClient, McpOfficialClient, Task> action,
        List<Exception> failures, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            var sdk = new KeyLoadClient(http, state.Identity.Secret, IntegrationClientOptions.Execution());
            await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, state.Identity.Secret, token);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => action(sdk, mcp, admin, official), failures);
        }, failures);
    }
}

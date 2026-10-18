using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Search;
using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPrincipalRevokedR44Trial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async admin =>
        {
            var created = await BlobPrincipalRevokedR44Seed.CreateAsync(fixture, admin, token);
            await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, created.Identity.Secret,
                async reader =>
            {
                await BlobPrincipalRevokedR44Pages.HealthyAsync(admin, reader, created, token);
                await BlobPrincipalRevokedR44Replay.RunAsync(admin, created, token);
                await BlobPrincipalRevokedR44Policy.ConfigureAsync(admin, created, true, token);
                await BlobPrincipalRevokedR44Refusals.LiveAsync(admin, reader, created, token);
                return true;
            }, token);
            return created;
        }, token);
        await NativeTextProtectedPublicColdRestart.RunAsync(fixture, token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async admin =>
        {
            await ColdDeniedAsync(fixture, admin, state, token);
            await BlobPrincipalRevokedR44Policy.ConfigureAsync(admin, state, false, token);
            await RepairedAsync(fixture, admin, state, token);
            return true;
        }, token);
        await NativeTextProtectedPublicColdRestart.RunAsync(fixture, token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async admin => { await RepairedAsync(fixture, admin, state, token); return true; }, token);
    }
    private static async Task ColdDeniedAsync(ClusterFixture fixture, RequestCqrsClusterCallers admin,
        BlobPrincipalRevokedR44State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
            var sdk = new KeyLoadClient(http, state.Identity.Secret, IntegrationClientOptions.Execution());
            await ServerFailureObserver.ObserveAsync(() => BlobPrincipalRevokedR44Refusals.SdkOnlyAsync(sdk, admin, state, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await BlobPrincipalRevokedR44Refusals.InitializeAsync(fixture, admin, state, token);
    }
    private static async Task RepairedAsync(ClusterFixture fixture, RequestCqrsClusterCallers admin,
        BlobPrincipalRevokedR44State state, CancellationToken token)
        => await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret,
            async reader =>
        {
            await BlobPrincipalRevokedR44Pages.HealthyAsync(admin, reader, state, token);
            await BlobPrincipalRevokedR44Replay.RunAsync(admin, state, token);
            return true;
        }, token);
}

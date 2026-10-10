using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferColdCallers
{
    internal static async Task WithAsync(ClusterFixture fixture, RemoteTransferColdSeed seed,
        Func<KeyLoadClient, McpOfficialClient, Task> execute, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
            var sdk = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
            await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
                seed.Identity.Secret, token);
            await ServerFailureObserver.ObserveAsync(() => execute(sdk, mcp), failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

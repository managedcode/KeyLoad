using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.EventStreams;

internal static class SharedDomainPublicSession
{
    internal static async Task RunAsync(ClusterFixture fixture, string key,
        Func<KeyLoadClient, McpOfficialClient, Task> body, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            var sdk = new KeyLoadClient(http, key, IntegrationClientOptions.Execution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var official = await McpOfficialClient.ConnectAsync(fixture,
                    McpCallerProtocol.Node2, key, token);
                await ServerFailureObserver.ObserveAsync(() => body(sdk, official), failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class SampleChunkSubnormalClusterCallers
{
    internal static async Task<T> RunOwnedAsync<T>(ClusterFixture fixture,
        Func<KeyLoadClient, McpOfficialClient, Task<T>> action, CancellationToken token)
    {
        var failures = new List<Exception>();
        T? result = default;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
                await using var mcp = await McpOfficialClient.ConnectAsync(fixture,
                    McpCallerProtocol.Node3, fixture.AdminKey, token).ConfigureAwait(false);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    result = await action(sdk, mcp).ConfigureAwait(false);
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result!;
    }
}

using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueDispatchCasClusterCallers
{
    internal static async Task<T> UseAsync<T>(ClusterFixture fixture, string node, string credential,
        Func<KeyLoadClient, McpOfficialClient, Task<T>> action, CancellationToken token)
    {
        var failures = new List<Exception>();
        T? result = default;
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var http = McpCallerHttp.Create(fixture, node);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var sdk = new KeyLoadClient(http, credential, IntegrationClientOptions.Execution());
                await using var mcp = await McpOfficialClient.ConnectAsync(fixture, node, credential, token);
                Task<T>? original = null;
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() =>
                {
                    var pending = action(sdk, mcp);
                    original = pending;
                    return pending;
                }, failures);
                if (original is { IsCompletedSuccessfully: true } completed)
                {
                    result = await completed;
                }
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return result!;
    }
}

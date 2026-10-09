using Aspire.Hosting;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3CallerOwner
{
    internal static async Task<T> RunAsync<T>(DistributedApplication app, string node, string actualCredential,
        Func<KeyLoadClient, McpOfficialClient, Task<T>> operation, CancellationToken cancellationToken)
    {
        HttpClient? http = null;
        McpOfficialClient? official = null;
        var failures = new List<Exception>();
        T result = default!;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            http = McpCallerHttp.Create(app, node);
            var sdk = new KeyLoadClient(http, actualCredential, IntegrationClientOptions.Execution());
            official = await McpOfficialClient.ConnectAsync(app, node, actualCredential, cancellationToken).ConfigureAwait(false);
            result = await operation(sdk, official).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (official is { } session)
        { await ServerFailureObserver.ObserveAsync(() => session.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (http is { } connection)
        { ServerFailureObserver.Observe(connection.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }
}

using Aspire.Hosting;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class RequestCqrsRf3Callers : IAsyncDisposable
{
    private readonly HttpClient sdkHttp;

    private RequestCqrsRf3Callers(HttpClient sdkHttp, KeyLoadClient sdk, McpOfficialClient mcp)
    { this.sdkHttp = sdkHttp; Sdk = sdk; Mcp = mcp; }

    internal KeyLoadClient Sdk { get; }
    internal McpOfficialClient Mcp { get; }

    internal static async Task<RequestCqrsRf3Callers> ConnectAsync(DistributedApplication app,
        string node, string adminKey, CancellationToken cancellationToken)
    {
        var http = McpCallerHttp.Create(app, node);
        try
        {
            var sdk = new KeyLoadClient(http, adminKey, IntegrationClientOptions.Execution());
            var mcp = await McpOfficialClient.ConnectAsync(app, node, adminKey, cancellationToken).ConfigureAwait(false);
            return new(http, sdk, mcp);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(http.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => Mcp.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(sdkHttp.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

using Aspire.Hosting;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class DueNoQuorumRf3Callers : IAsyncDisposable
{
    private readonly HttpClient sdkHttp;

    private DueNoQuorumRf3Callers(HttpClient sdkHttp, KeyLoadClient sdk, McpOfficialClient mcp)
    { this.sdkHttp = sdkHttp; Sdk = sdk; Mcp = mcp; }

    internal KeyLoadClient Sdk { get; }
    internal McpOfficialClient Mcp { get; }

    internal static async Task<DueNoQuorumRf3Callers> ConnectAsync(DistributedApplication app,
        string node, string credential, CancellationToken cancellationToken)
    {
        var http = McpCallerHttp.Create(app, node);
        try
        {
            var sdk = new KeyLoadClient(http, credential, IntegrationClientOptions.Execution());
            var mcp = await McpOfficialClient.ConnectAsync(app, node, credential, cancellationToken)
                .ConfigureAwait(false);
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

using Aspire.Hosting;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal sealed class NodeEpochRf3Callers : IAsyncDisposable
{
    private readonly HttpClient sdkHttp;

    private NodeEpochRf3Callers(HttpClient sdkHttp, KeyLoadClient sdk, McpOfficialClient mcp)
    { this.sdkHttp = sdkHttp; Sdk = sdk; Mcp = mcp; }

    internal KeyLoadClient Sdk { get; }
    internal McpOfficialClient Mcp { get; }

    internal static async Task<NodeEpochRf3Callers> ConnectAsync(DistributedApplication app,
        string sdkNode, string mcpNode, string key, CancellationToken cancellationToken)
    {
        var sdkHttp = McpCallerHttp.Create(app, sdkNode);
        try
        {
            var sdk = new KeyLoadClient(sdkHttp, key, IntegrationClientOptions.Execution());
            var mcp = await McpOfficialClient.ConnectAsync(app, mcpNode, key, cancellationToken).ConfigureAwait(false);
            return new(sdkHttp, sdk, mcp);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(sdkHttp.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var cleanup = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(DisposeMcpBoundedAsync, cleanup).ConfigureAwait(false);
        ServerFailureObserver.Observe(sdkHttp.Dispose, cleanup);
        ServerFailureObserver.ThrowIfAny(cleanup);
    }

    private async Task DisposeMcpBoundedAsync()
    {
        var disposal = Mcp.DisposeAsync().AsTask();
        var timeout = Task.Delay(NodeEpochRf3Protocol.CleanupDeadline, TimeProvider.System);
        if (await Task.WhenAny(disposal, timeout).ConfigureAwait(false) != disposal)
        {
            _ = disposal.ContinueWith(static task => _ = task.Exception,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw new TimeoutException("The official MCP client did not dispose within its cleanup bound.");
        }
        await disposal.ConfigureAwait(false);
    }
}

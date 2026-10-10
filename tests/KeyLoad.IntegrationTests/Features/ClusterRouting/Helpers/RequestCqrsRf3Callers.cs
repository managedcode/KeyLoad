using System.Net;
using System.Text.Json;
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

    internal async Task AssertFreshRoutingReadyAsync(CancellationToken cancellationToken)
    {
        using var response = await sdkHttp.GetAsync(RequestCqrsRf3Protocol.ReadyUri,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[RequestCqrsRf3Protocol.ReadyReplyMaximumBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await body.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            { break; }
            length += read;
        }
        await Assert.That(length).IsLessThanOrEqualTo(RequestCqrsRf3Protocol.ReadyReplyMaximumBytes);
        using var reply = JsonDocument.Parse(buffer.AsMemory(0, length));
        await Assert.That(reply.RootElement.EnumerateObject().Count()).IsEqualTo(RequestCqrsRf3Protocol.ReadyReplyPropertyCount);
        await Assert.That(reply.RootElement.GetProperty(RequestCqrsRf3Protocol.ReadyReplyStatusProperty).GetString()).IsEqualTo(RequestCqrsRf3Protocol.ReadyReplyExpectedStatus);
        await Assert.That(reply.RootElement.GetProperty(RequestCqrsRf3Protocol.ReadyReplyVotersProperty).GetInt32()).IsEqualTo(RequestCqrsRf3Protocol.NodeCount);
    }

    internal static async Task<RequestCqrsRf3Callers> ConnectAsync(DistributedApplication app,
        string node, string adminKey, CancellationToken cancellationToken)
    {
        var http = McpCallerHttp.Create(app, node);
        return await ConnectOwnedAsync(app, node, adminKey, http, cancellationToken).ConfigureAwait(false);
    }

    internal static Task<RequestCqrsRf3Callers> ConnectObservedAsync(DistributedApplication app,
        string node, string adminKey, HttpClient observedSdkHttp, CancellationToken cancellationToken)
        => ConnectOwnedAsync(app, node, adminKey, observedSdkHttp, cancellationToken);

    private static async Task<RequestCqrsRf3Callers> ConnectOwnedAsync(DistributedApplication app,
        string node, string adminKey, HttpClient http, CancellationToken cancellationToken)
    {
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

    internal static async Task<T> RunOwnedAsync<T>(DistributedApplication app,
        string node, string adminKey, Func<RequestCqrsRf3Callers, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        T result = default!;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var callers = await ConnectAsync(app, node, adminKey, cancellationToken);
            await ServerFailureObserver.ObserveAsync(async () =>
            { result = await operation(callers); }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => Mcp.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(sdkHttp.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

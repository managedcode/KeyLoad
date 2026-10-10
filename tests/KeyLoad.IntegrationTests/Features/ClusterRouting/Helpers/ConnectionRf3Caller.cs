using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class ConnectionRf3Caller : IAsyncDisposable
{
    private readonly SocketsHttpHandler sockets;
    private readonly HttpClient http;
    private readonly HttpClientTransport? transport;
    private McpClient? mcp;
    private bool disposed;

    private ConnectionRf3Caller(DistributedApplication app, string node, string key, bool multiplexed)
    {
        sockets = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = ConnectionRf3Protocol.OneConnection,
            EnableMultipleHttp2Connections = false,
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            UseProxy = false,
            ConnectTimeout = new ConnectionRf3Options().ConnectTimeout
        };
        http = new HttpClient(sockets, disposeHandler: true)
        {
            BaseAddress = app.GetEndpoint(node, multiplexed ? ConnectionRf3Protocol.Http2Endpoint : McpCallerProtocol.HttpEndpoint),
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = multiplexed ? HttpVersion.Version20 : HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        Sdk = new KeyLoadClient(http, key, IntegrationClientOptions.Execution());
        if (multiplexed)
        { return; }
        transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress, McpCallerProtocol.Endpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
            EnableStandaloneGetStream = false,
            AdditionalHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
            { [McpCallerProtocol.AuthorizationHeader] = McpCallerProtocol.BearerPrefix + key }
        }, http);
    }

    internal KeyLoadClient Sdk { get; }
    internal McpClient Mcp => mcp ?? throw new InvalidOperationException(ConnectionRf3Protocol.MissingOwner);
    internal KeyLoadClient WithCredential(string key) => new(http, key, IntegrationClientOptions.Execution());

    internal static async Task<ConnectionRf3Caller> ConnectAsync(DistributedApplication app, string node,
        string key, CancellationToken cancellationToken, bool multiplexed = false)
    {
        var owner = new ConnectionRf3Caller(app, node, key, multiplexed);
        try
        {
            if (owner.transport is not null)
            {
                owner.mcp = await McpClient.CreateAsync(owner.transport,
                    new McpClientOptions { ProtocolVersion = McpCallerProtocol.ProtocolVersion },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            return owner;
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            await ServerFailureObserver.ObserveAsync(() => owner.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal Task<CallToolResult> CallAsync<T>(string toolName, T request, CancellationToken cancellationToken)
        => Mcp.InvokeKeyLoadToolAsync(toolName, McpOfficialClient.Arguments(request), cancellationToken).AsTask();

    internal void AbortTransport(List<Exception> failures)
    {
        try
        { http.Dispose(); }
        catch (Exception error) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        { sockets.Dispose(); }
        catch (Exception error) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        { return; }
        disposed = true;
        var failures = new List<Exception>();
        if (mcp is not null)
        {
            try
            { await mcp.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        if (transport is not null)
        {
            try
            { await transport.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        try
        { http.Dispose(); }
        catch (Exception error) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        { sockets.Dispose(); }
        catch (Exception error) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

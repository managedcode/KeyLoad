using System.Text.Json;
using Aspire.Hosting;
using KeyLoad.Server;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>Owns the actual official SDK client, native HTTP transport and Aspire HTTP connection.</summary>
internal sealed class McpOfficialClient : IAsyncDisposable
{
    private readonly HttpClient http;
    private readonly HttpClientTransport transport;
    private McpClient? client;

    private McpOfficialClient(HttpClient http, HttpClientTransport transport)
    {
        this.http = http;
        this.transport = transport;
    }

    /// <summary>Gets the genuine connected client; no protocol calls are reimplemented here.</summary>
    internal McpClient Client => client ?? throw new InvalidOperationException(McpCallerProtocol.MissingClient);

    /// <summary>Connects using discovery-first stateless HTTP and the supplied actual bearer credential.</summary>
    /// <param name="fixture">The real initialized RF3 cluster.</param>
    /// <param name="node">The actual Aspire endpoint resource.</param>
    /// <param name="key">The actual credential, or null to test a missing bearer header.</param>
    /// <param name="cancellationToken">The external bounded caller lifetime.</param>
    /// <returns>The owner of the actual SDK and HTTP resources.</returns>
    internal static Task<McpOfficialClient> ConnectAsync(ClusterFixture fixture, string node,
        string? key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        return ConnectAsync(fixture.App, node, key, cancellationToken);
    }

    /// <summary>Connects the official SDK through a genuine caller-owned Aspire application wave.</summary>
    /// <param name="app">The actual running Aspire application.</param>
    /// <param name="node">The actual Aspire endpoint resource.</param>
    /// <param name="key">The persisted credential used at the public boundary.</param>
    /// <param name="cancellationToken">The bounded caller lifetime.</param>
    /// <returns>The actual native SDK, transport and HTTP resource owner.</returns>
    internal static async Task<McpOfficialClient> ConnectAsync(DistributedApplication app, string node,
        string? key, CancellationToken cancellationToken)
    {
        var connection = McpCallerHttp.Create(app, node);
        var transport = CreateTransport(connection, key);
        var owner = new McpOfficialClient(connection, transport);
        try
        {
            await owner.InitializeAsync(cancellationToken).ConfigureAwait(false);
            return owner;
        }
        catch (Exception primary)
        {
            try
            { await owner.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup)
            { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }

    private static HttpClientTransport CreateTransport(HttpClient connection, string? key)
    {
        try
        {
            var options = new HttpClientTransportOptions
            {
                Endpoint = new Uri(connection.BaseAddress ?? throw new InvalidOperationException(McpCallerProtocol.MissingEndpoint), McpCallerProtocol.Endpoint),
                TransportMode = HttpTransportMode.StreamableHttp,
                EnableStandaloneGetStream = false,
                AdditionalHeaders = key is null ? null : new Dictionary<string, string>(StringComparer.Ordinal)
                { [McpCallerProtocol.AuthorizationHeader] = McpCallerProtocol.BearerPrefix + key }
            };
            return new HttpClientTransport(options, connection);
        }
        catch (Exception)
        {
            connection.Dispose();
            throw;
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        client = await McpClient.CreateAsync(transport, new McpClientOptions(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Calls the official tool API with the actual canonical typed request serialized at its public boundary.</summary>
    /// <typeparam name="T">The canonical request type.</typeparam>
    /// <param name="toolName">The frozen public operation name.</param>
    /// <param name="request">The actual canonical request value.</param>
    /// <param name="cancellationToken">The bounded external caller token.</param>
    /// <returns>The official SDK's actual tool result.</returns>
    internal Task<CallToolResult> CallAsync<T>(string toolName, T request, CancellationToken cancellationToken)
        => Client.CallToolAsync(toolName, Arguments(request), cancellationToken: cancellationToken).AsTask();

    /// <summary>Builds only public tool arguments from the exact canonical serializer.</summary>
    /// <typeparam name="T">The actual canonical request type.</typeparam>
    /// <param name="request">The public request, without trusted caller roles.</param>
    /// <returns>The argument dictionary passed directly to the native client.</returns>
    internal static Dictionary<string, object?> Arguments<T>(T request) => new(StringComparer.Ordinal)
    { [McpCallerProtocol.Request] = JsonSerializer.SerializeToElement(request, JsonDefaults.Options) };

    /// <summary>Drains the actual native client before disposing its transport and real HTTP connection.</summary>
    /// <returns>The completed native resource cleanup.</returns>
    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        if (client is { } native)
        { await ServerFailureObserver.ObserveAsync(() => native.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        await ServerFailureObserver.ObserveAsync(() => transport.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(http.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

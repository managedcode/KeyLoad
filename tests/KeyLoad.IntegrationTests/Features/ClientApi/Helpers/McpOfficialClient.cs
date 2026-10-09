using System.Text.Json;
using Aspire.Hosting;
using KeyLoad.Orleans;
using KeyLoad.Server;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>Owns the actual official SDK client, native HTTP transport and Aspire HTTP connection.</summary>
internal sealed class McpOfficialClient : IAsyncDisposable
{
    private readonly HttpClient http;
    private readonly HttpClientTransport transport;
    private readonly McpInitializeHttpObservation observation;
    private McpClient? client;

    private McpOfficialClient(HttpClient http, HttpClientTransport transport, McpInitializeHttpObservation observation)
    {
        this.http = http;
        this.transport = transport;
        this.observation = observation;
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
    internal static Task<McpOfficialClient> ConnectAsync(DistributedApplication app, string node,
        string? key, CancellationToken cancellationToken)
        => ConnectAsync(app, node, key, McpCallerProtocol.ProtocolVersion, cancellationToken);

    /// <summary>Submits an explicit official protocol revision without changing native transport validation.</summary>
    /// <param name="app">The actual running Aspire application.</param>
    /// <param name="node">The discovered resource endpoint.</param>
    /// <param name="key">The actual persisted credential.</param>
    /// <param name="protocolVersion">The exact revision offered by the official SDK.</param>
    /// <param name="cancellationToken">The original bounded caller lifetime.</param>
    /// <returns>The connected native owner after successful official admission.</returns>
    internal static async Task<McpOfficialClient> ConnectAsync(DistributedApplication app, string node,
        string? key, string protocolVersion, CancellationToken cancellationToken)
    {
        var connection = McpCallerHttp.CreateObserved(app, node, out var observation);
        HttpClientTransport? transport = null;
        McpOfficialClient? owner = null;
        try
        {
            transport = CreateTransport(connection, key);
            owner = new McpOfficialClient(connection, transport, observation);
            await owner.InitializeAsync(protocolVersion, node, cancellationToken).ConfigureAwait(false);
            return owner;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            if (owner is not null)
            { await ServerFailureObserver.ObserveAsync(() => owner.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
            else
            { await DisposeUntransferredAsync(transport, connection, observation, failures).ConfigureAwait(false); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private static async Task DisposeUntransferredAsync(HttpClientTransport? transport, HttpClient connection,
        McpInitializeHttpObservation observation, List<Exception> failures)
    {
        if (transport is not null)
        { await ServerFailureObserver.ObserveAsync(() => transport.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.Observe(connection.Dispose, failures);
        ServerFailureObserver.Observe(observation.Dispose, failures);
    }

    private static HttpClientTransport CreateTransport(HttpClient connection, string? key)
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

    private async Task InitializeAsync(string protocolVersion, string node, CancellationToken cancellationToken)
    {
        try
        {
            client = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = protocolVersion },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception original)
        {
            observation.WriteAndThrow(original, node);
            throw;
        }
        finally
        { observation.Stop(); }
    }

    /// <summary>Calls the official tool API with the actual canonical typed request serialized at its public boundary.</summary>
    /// <typeparam name="T">The canonical request type.</typeparam>
    /// <param name="toolName">The frozen public operation name.</param>
    /// <param name="request">The actual canonical request value.</param>
    /// <param name="cancellationToken">The bounded external caller token.</param>
    /// <returns>The official SDK's actual tool result.</returns>
    internal Task<CallToolResult> CallAsync<T>(string toolName, T request, CancellationToken cancellationToken)
        => Client.InvokeKeyLoadToolAsync(toolName, Arguments(request), cancellationToken).AsTask();

    /// <summary>Invokes a bodyless native operation with its exact empty argument contract.</summary>
    /// <param name="toolName">The frozen public bodyless operation name.</param>
    /// <param name="cancellationToken">The bounded external caller token.</param>
    /// <returns>The official SDK's actual tool result.</returns>
    internal Task<CallToolResult> CallWithoutBodyAsync(string toolName, CancellationToken cancellationToken)
        => Client.InvokeKeyLoadToolAsync(toolName, new Dictionary<string, object?>(StringComparer.Ordinal),
            cancellationToken).AsTask();

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
        try
        { observation.Dispose(); }
        catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

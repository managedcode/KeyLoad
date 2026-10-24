using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.RecoveryTests.OrleansExecution;
using KeyLoad.Server;
using ModelContextProtocol.Client;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class AuthenticationExecutionClients
{
    internal static async Task RunAsync(RequestCqrsHostedRuntimeFixture host, AuthenticationExecutionState state,
        AuthenticationExecutionCollector collector,
        Func<KeyLoadClient, McpClient, AuthenticationExecutionHttpHandler, Task> operation, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var inner = new HttpClientHandler();
            using var handler = new AuthenticationExecutionHttpHandler(inner);
            using var http = new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = host.PublicEndpoint,
                Timeout = Timeout.InfiniteTimeSpan
            };
            StandardAspNetAuthorityTrial.AddHeaders(http);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var transport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(http.BaseAddress, McpCallerProtocol.Endpoint),
                    TransportMode = HttpTransportMode.StreamableHttp,
                    EnableStandaloneGetStream = false,
                    AdditionalHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [McpCallerProtocol.AuthorizationHeader] = McpCallerProtocol.BearerPrefix + state.Secret
                    }
                }, http);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var position = collector.Position;
                    var sent = handler.Requests;
                    await using var client = await McpClient.CreateAsync(transport,
                        new McpClientOptions { ProtocolVersion = McpCallerProtocol.ProtocolVersion },
                        cancellationToken: token);
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        await client.DiscoverKeyLoadToolAsync(McpCallerTools.DocumentsGet, token);
                        await AuthenticationExecutionAssertions.WindowAsync(collector, position,
                            handler.Requests - sent, state.Reader.Id);
                        await operation(new KeyLoadClient(http, state.Secret, IntegrationClientOptions.Execution()),
                            client, handler);
                    }, failures);
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static ValueTask<ModelContextProtocol.Protocol.CallToolResult> ReadAsync(
        McpClient client, AuthenticationExecutionState state, CancellationToken token)
        => client.InvokeKeyLoadToolAsync(McpCallerTools.DocumentsGet, new Dictionary<string, object?>
        {
            [McpCallerProtocol.Request] = JsonSerializer.SerializeToElement(new GetDocumentRequest(state.Document.Reference), JsonDefaults.Options)
        }, token);
}

using System.Net;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using ModelContextProtocol.Client;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class FoundationOtlpRf3RejectedHandshake
{
    internal static async Task RunAsync(ClusterFixture fixture, string node, string key, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var http = McpCallerHttp.Create(fixture, node);
            http.DefaultRequestHeaders.Add(FoundationOtlpRf3Protocol.Header, FoundationOtlpRf3Protocol.HeaderCanary);
            await using var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, McpCallerProtocol.Endpoint),
                TransportMode = HttpTransportMode.StreamableHttp,
                EnableStandaloneGetStream = false,
                AdditionalHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [McpCallerProtocol.AuthorizationHeader] = McpCallerProtocol.BearerPrefix + key
                }
            }, http);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var error = await Assert.ThrowsAsync<HttpRequestException>(async () =>
                {
                    await using var rejected = await McpClient.CreateAsync(transport,
                        new McpClientOptions { ProtocolVersion = McpCallerProtocol.UnsupportedProtocolVersion },
                        cancellationToken: token);
                });
                await Assert.That(error?.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
                await Assert.That(error?.Message.Contains(key, StringComparison.Ordinal) == true).IsFalse();
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

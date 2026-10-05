using System.Net.Http.Headers;
using System.Text;
using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3McpGuardEvidenceCall
{
    internal const string MalformedPayload = "{\"method\":\"tools/list\",\"params\":{}}";
    private const string JsonMediaType = "application/json";
    private const string ToolsCallMethod = "tools/call";

    internal static async Task SendMalformedAsync(DistributedApplication app, string node, string persistedKey,
        CancellationToken cancellationToken)
    {
        using var client = McpCallerHttp.Create(app, node);
        using var request = CreateRequest(persistedKey);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await RequestCqrsRf3McpGuardEvidenceCallAssertions.VerifyValidationAsync(response, cancellationToken)
            .ConfigureAwait(false);
    }

    private static HttpRequestMessage CreateRequest(string persistedKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, McpCallerProtocol.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue(McpCallerProtocol.BearerScheme, persistedKey);
        request.Headers.TryAddWithoutValidation(McpTransportProtocol.RevisionHeader, McpTransportProtocol.Revision);
        request.Headers.TryAddWithoutValidation(McpTransportProtocol.MethodHeader, ToolsCallMethod);
        request.Content = new StringContent(MalformedPayload, Encoding.UTF8, JsonMediaType);
        return request;
    }
}

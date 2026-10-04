using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>Observes the actual pre-handler HTTP authentication boundary without hand-building an MCP message.</summary>
internal static class McpUnauthorizedProbe
{
    /// <summary>Checks native HTTP 401, absence of any operation GUID and the canonical safe Problem.</summary>
    /// <param name="fixture">The actual RF3 application.</param>
    /// <param name="node">The native Aspire endpoint resource.</param>
    /// <param name="key">The rejected credential or absent bearer.</param>
    /// <param name="cancellationToken">The external bounded caller token.</param>
    /// <returns>The completed real HTTP response assertions.</returns>
    internal static async Task VerifyAsync(ClusterFixture fixture, string node, string? key, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, node);
        using var request = new HttpRequestMessage(HttpMethod.Get, McpCallerProtocol.Endpoint);
        if (key is not null)
        { request.Headers.Authorization = new AuthenticationHeaderValue(McpCallerProtocol.BearerScheme, key); }
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.Contains(McpCallerProtocol.RequestHeader)).IsFalse();
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        await Assert.That(body.Length).IsLessThanOrEqualTo(McpCallerProtocol.ErrorByteLimit);
        using var problem = JsonDocument.Parse(body);
        await McpCallerAssertions.VerifyProblemAsync(problem.RootElement, ErrorCode.Unauthenticated);
        if (key is not null)
        { await Assert.That(Encoding.UTF8.GetString(body).Contains(key, StringComparison.Ordinal)).IsFalse(); }
    }
}

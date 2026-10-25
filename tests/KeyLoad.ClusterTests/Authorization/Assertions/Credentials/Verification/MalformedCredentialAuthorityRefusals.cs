using System.Net;
using System.Text;
using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.DocumentStorage;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Authorization;

internal static class MalformedCredentialAuthorityRefusals
{
    private const string UnsupportedSchemePrefix = "Basic ";
    private const string ExpectedType = "urn:keyload:error:Unauthenticated";
    private const string ExpectedDetail = "An API key is required.";

    internal static async Task VerifyAsync(ClusterFixture fixture, global::KeyLoad.Client.KeyLoadClient root,
        InitialCredentialAuthorityState state, CancellationToken token)
    {
        var before = await InitialCredentialAuthorityValues.ImageAsync(root, state, token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        foreach (var header in new[] { UnsupportedSchemePrefix + state.Identity.Secret, McpCallerProtocol.BearerScheme })
        {
            await RefusedAsync(http, header, state.Identity.Secret, token);
            await SqlClusterProtocol.EqualAsync(before, await InitialCredentialAuthorityValues.ImageAsync(root, state, token));
        }
    }

    private static async Task RefusedAsync(HttpClient http, string header, string secret, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, McpCallerProtocol.Endpoint);
        await Assert.That(request.Headers.TryAddWithoutValidation(McpCallerProtocol.AuthorizationHeader, header)).IsTrue();
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.Contains(McpCallerProtocol.RequestHeader)).IsFalse();
        var body = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
        await Assert.That(body.Length).IsLessThanOrEqualTo(McpCallerProtocol.ErrorByteLimit);
        using var problem = JsonDocument.Parse(body);
        await McpCallerAssertions.VerifyProblemAsync(problem.RootElement, ErrorCode.Unauthenticated);
        await Assert.That(problem.RootElement.GetProperty(McpCallerProtocol.ProblemType).GetString()).IsEqualTo(ExpectedType);
        await Assert.That(problem.RootElement.GetProperty(McpCallerProtocol.ProblemTitle).GetString()).IsEqualTo(nameof(ErrorCode.Unauthenticated));
        await Assert.That(problem.RootElement.GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(ExpectedDetail);
        var text = Encoding.UTF8.GetString(body);
        await Assert.That(text.Contains(secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(McpDocumentProtocol.InitialJson, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal)).IsFalse();
    }
}

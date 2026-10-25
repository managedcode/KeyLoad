using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.DocumentStorage;
using KeyLoad.ProcessRecoveryTests.OrleansExecution;

namespace KeyLoad.ClusterTests.Authorization;

internal static class AuthenticationExecutionHttpRefusal
{
    internal static async Task RunAsync(RequestCqrsHostedRuntimeFixture host,
        AuthenticationExecutionState state, AuthenticationExecutionCollector collector, CancellationToken token)
    {
        using var rootHttp = new HttpClient { BaseAddress = host.PublicEndpoint, Timeout = Timeout.InfiniteTimeSpan };
        var root = new KeyLoadClient(rootHttp, host.AdminKey, IntegrationClientOptions.Execution());
        var before = await AuthenticationExecutionValues.ImageAsync(root, state, token);
        foreach (var (header, entered) in new (string? Header, bool Entered)[]
        {
            (null, false),
            (AuthenticationExecutionProtocol.MalformedScheme + state.Secret, false),
            (McpCallerProtocol.BearerPrefix + state.Secret + AuthenticationExecutionProtocol.UnknownSuffix, true)
        })
        {
            using var inner = new HttpClientHandler();
            using var handler = new AuthenticationExecutionHttpHandler(inner);
            using var http = new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = rootHttp.BaseAddress,
                Timeout = Timeout.InfiniteTimeSpan
            };
            StandardAspNetAuthorityTrial.AddHeaders(http);
            using var request = new HttpRequestMessage(HttpMethod.Post, AuthenticationExecutionProtocol.DocumentsRoute)
            {
                Content = JsonContent.Create(new GetDocumentRequest(state.Document.Reference), options: JsonDefaults.Options)
            };
            if (header is not null)
            {
                request.Headers.TryAddWithoutValidation(McpCallerProtocol.AuthorizationHeader, header);
            }
            var position = collector.Position;
            using var response = await http.SendAsync(request, token);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            var bytes = await response.Content.ReadAsByteArrayAsync(token);
            await Assert.That(bytes.Length).IsLessThanOrEqualTo(McpCallerProtocol.ErrorByteLimit);
            using var problem = JsonDocument.Parse(bytes);
            await McpCallerAssertions.VerifyProblemAsync(problem.RootElement, ErrorCode.Unauthenticated);
            await Assert.That(response.Headers.Contains(McpCallerProtocol.RequestHeader)).IsFalse();
            var text = System.Text.Encoding.UTF8.GetString(bytes);
            await Assert.That(text.Contains(state.Secret, StringComparison.Ordinal)).IsFalse();
            await Assert.That(text.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal)).IsFalse();
            await AuthenticationExecutionAssertions.WindowAsync(collector, position, handler.Requests, subject: null, entered);
            await Assert.That(await AuthenticationExecutionValues.ImageAsync(root, state, token)).IsEqualTo(before);
        }
        await Assert.That(await AuthenticationExecutionValues.ImageAsync(root, state, token)).IsEqualTo(before);
    }
}

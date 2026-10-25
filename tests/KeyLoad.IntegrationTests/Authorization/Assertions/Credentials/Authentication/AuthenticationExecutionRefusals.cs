using System.Net;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.DocumentStorage;
using ModelContextProtocol.Client;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class AuthenticationExecutionRefusals
{
    internal static async Task PermissionAsync(KeyLoadClient root, KeyLoadClient reader, McpClient mcp,
        AuthenticationExecutionHttpHandler http, AuthenticationExecutionCollector collector,
        AuthenticationExecutionState state, CancellationToken token)
    {
        var before = await AuthenticationExecutionValues.ImageAsync(root, state, token);
        var position = collector.Position;
        var sent = http.Requests;
        var denied = await reader.GetAsync(state.Document.Reference, token);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        var safeSdk = System.Text.Json.JsonSerializer.Serialize(denied, JsonDefaults.Options);
        await Assert.That(safeSdk.Contains(state.Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(safeSdk.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal)).IsFalse();
        await AuthenticationExecutionAssertions.WindowAsync(collector, position, http.Requests - sent, state.Reader.Id);
        await Assert.That(await AuthenticationExecutionValues.ImageAsync(root, state, token)).IsEqualTo(before);
        position = collector.Position;
        sent = http.Requests;
        var reply = await AuthenticationExecutionClients.ReadAsync(mcp, state, token);
        _ = (await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, dispatched: true)).Value;
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Secret, McpDocumentProtocol.PrivateValue);
        await AuthenticationExecutionAssertions.WindowAsync(collector, position, http.Requests - sent, state.Reader.Id);
        await Assert.That(await AuthenticationExecutionValues.ImageAsync(root, state, token)).IsEqualTo(before);
    }

    internal static async Task UnauthenticatedAsync(KeyLoadClient root, KeyLoadClient reader, McpClient mcp,
        AuthenticationExecutionHttpHandler http, AuthenticationExecutionCollector collector,
        AuthenticationExecutionState state, CancellationToken token)
    {
        var before = await AuthenticationExecutionValues.ImageAsync(root, state, token);
        var position = collector.Position;
        var sent = http.Requests;
        var denied = await reader.GetAsync(state.Document.Reference, token);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
        var safeSdk = System.Text.Json.JsonSerializer.Serialize(denied, JsonDefaults.Options);
        await Assert.That(safeSdk.Contains(state.Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(safeSdk.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal)).IsFalse();
        await AuthenticationExecutionAssertions.WindowAsync(collector, position, http.Requests - sent, subject: null);
        await Assert.That(await AuthenticationExecutionValues.ImageAsync(root, state, token)).IsEqualTo(before);
        position = collector.Position;
        sent = http.Requests;
        var original = await Assert.ThrowsAsync<HttpRequestException>(() =>
            AuthenticationExecutionClients.ReadAsync(mcp, state, token).AsTask());
        await Assert.That(original?.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(original?.Message.Contains(state.Secret, StringComparison.Ordinal) == true).IsFalse();
        await AuthenticationExecutionAssertions.WindowAsync(collector, position, http.Requests - sent, subject: null);
        await Assert.That(await AuthenticationExecutionValues.ImageAsync(root, state, token)).IsEqualTo(before);
    }
}

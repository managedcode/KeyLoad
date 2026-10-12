using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using ModelContextProtocol.Client;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class AuthenticationExecutionWriteRefusal
{
    internal static async Task RunAsync(KeyLoadClient root, KeyLoadClient reader, McpClient mcp,
        AuthenticationExecutionHttpHandler http, AuthenticationExecutionCollector collector,
        AuthenticationExecutionState state, CancellationToken token)
    {
        var before = await AuthenticationExecutionValues.ImageAsync(root, state, token);
        var deniedCommand = state.Document.Command(Guid.NewGuid(), McpDocumentProtocol.ConflictingJson);
        var position = collector.Position;
        var sent = http.Requests;
        var denied = await reader.CommitAsync(deniedCommand, token);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await AuthenticationExecutionAssertions.WindowAsync(collector, position, http.Requests - sent, state.Reader.Id);
        await Assert.That(await AuthenticationExecutionValues.ImageAsync(root, state, token)).IsEqualTo(before);
        position = collector.Position;
        sent = http.Requests;
        var reply = await mcp.InvokeKeyLoadToolAsync(McpCallerTools.DocumentsCommit,
            McpOfficialClient.Arguments(deniedCommand), token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Secret, McpDocumentProtocol.PrivateValue);
        await AuthenticationExecutionAssertions.WindowAsync(collector, position, http.Requests - sent, state.Reader.Id);
        await Assert.That(await AuthenticationExecutionValues.ImageAsync(root, state, token)).IsEqualTo(before);
    }
}

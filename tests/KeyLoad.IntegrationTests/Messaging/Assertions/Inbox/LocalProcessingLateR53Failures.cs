using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class LocalProcessingLateR53Failures
{
    internal static async Task SdkAsync(ManagedCode.Communication.Result<CommitReceipt> result, ErrorCode code, string detail)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await InboxProcessingClusterAssertions.EqualAsync(result.Problem, Errors.Problem(code, detail));
    }

    internal static async Task McpAsync(ModelContextProtocol.Protocol.CallToolResult reply, ErrorCode code)
    {
        _ = (await McpCallerAssertions.ErrorAsync(reply, code, true)).Value;
        var envelope = reply.StructuredContent ?? throw new InvalidOperationException(LocalProcessingLateR53Protocol.Missing);
        await Assert.That(envelope.GetProperty(McpCallerProtocol.Error).GetProperty(McpCallerProtocol.ProblemDetail).GetString())
            .IsEqualTo(code == ErrorCode.PermissionDenied ? LocalProcessingLateR53Protocol.SafePermission : LocalProcessingLateR53Protocol.SafeGeneric);
    }

    internal static async Task MixedAsync(LocalProcessingLateR53State state, ProcessingRequest request,
        ErrorCode code, string detail, KeyLoadClient sdk, McpOfficialClient mcp,
        KeyLoadClient admin, McpOfficialClient official, CancellationToken token)
    {
        await SdkAsync(await sdk.CommitProcessingAsync(request, token), code, detail);
        await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
        await McpAsync(await mcp.CallAsync(McpCallerTools.MessagesProcess, request, token), code);
        await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
    }
}

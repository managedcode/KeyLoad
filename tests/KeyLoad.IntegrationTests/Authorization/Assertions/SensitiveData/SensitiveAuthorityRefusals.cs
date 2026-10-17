using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SensitiveAuthorityRefusals
{
    internal static async Task PatchAsync(KeyLoadClient root, KeyLoadClient reader, McpOfficialClient session,
        McpDocumentScenario scenario, McpPersistedIdentity identity, CommandRequest command,
        bool patched, CancellationToken token)
    {
        var before = await SensitiveAuthorityAssertions.ImageAsync(root, scenario, patched, token);
        var mcp = await session.CallAsync(McpCallerTools.DocumentsCommit, command, token);
        await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(mcp, identity.Secret, McpDocumentProtocol.ConflictValue);
        await McpCallerAssertions.DoesNotDiscloseAsync(mcp, identity.Secret, McpDocumentProtocol.PrivateValue);
        var sdk = await reader.CommitAsync(command, token);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        var result = JsonSerializer.Serialize(sdk, JsonDefaults.Options);
        await Assert.That(result.Contains(identity.Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(result.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal)).IsFalse();
        await Assert.That(result.Contains(McpDocumentProtocol.ConflictValue, StringComparison.Ordinal)).IsFalse();
        await SqlRf3Protocol.EqualAsync(before,
            await SensitiveAuthorityAssertions.ImageAsync(root, scenario, patched, token));
    }
}

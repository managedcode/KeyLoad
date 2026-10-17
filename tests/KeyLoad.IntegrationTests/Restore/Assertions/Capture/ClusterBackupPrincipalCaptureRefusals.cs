using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterBackupPrincipalCaptureRefusals
{
    internal static async Task RequireAsync(KeyLoadClient sdk, McpOfficialClient official, PartitionRef partition,
        ClusterBackupPrincipalCaptureState state, Func<Task> current, CancellationToken token)
    {
        await SdkAsync(await sdk.CaptureClusterBackupOwnerAsync(state.Request, token).ConfigureAwait(false), state);
        await current().ConfigureAwait(false);
        await McpAsync(await official.CallAsync(ClusterRestoreRf3Protocol.CaptureTool, state.Request,
            token).ConfigureAwait(false), state);
        await current().ConfigureAwait(false);
        var sql = SqlRf3Protocol.Call(partition, ClusterRestoreRf3Protocol.CaptureTool, state.Request);
        await SqlAsync(await sdk.ExecuteSqlAsync(sql, token).ConfigureAwait(false), state);
        await current().ConfigureAwait(false);
        await McpAsync(await official.CallAsync(SqlOperationProtocol.ToolName, sql, token).ConfigureAwait(false), state);
        await current().ConfigureAwait(false);
    }

    private static async Task SdkAsync(Result<ClusterBackupOwnerReceipt> actual, ClusterBackupPrincipalCaptureState state)
    {
        await Assert.That(actual.IsSuccess).IsFalse();
        await Assert.That(actual.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied,
            ClusterBackupPrincipalCaptureProtocol.SdkDenied), actual.Problem);
        await PrivateAsync(JsonSerializer.Serialize(actual.Problem, JsonDefaults.Options), state);
    }

    private static async Task SqlAsync(Result<JsonElement> actual, ClusterBackupPrincipalCaptureState state)
    {
        await Assert.That(actual.IsSuccess).IsFalse();
        await Assert.That(actual.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied,
            ClusterBackupPrincipalCaptureProtocol.SdkDenied), actual.Problem);
        await PrivateAsync(JsonSerializer.Serialize(actual.Problem, JsonDefaults.Options), state);
    }

    private static async Task McpAsync(CallToolResult actual, ClusterBackupPrincipalCaptureState state)
    {
        await McpCallerAssertions.ErrorAsync(actual, ErrorCode.PermissionDenied, dispatched: true);
        var expected = JsonSerializer.SerializeToElement(Errors.Problem(ErrorCode.PermissionDenied,
            ClusterBackupPrincipalCaptureProtocol.McpDenied), JsonDefaults.Options);
        await Assert.That(JsonElement.DeepEquals(expected,
            actual.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error))).IsTrue();
        await PrivateAsync(JsonSerializer.Serialize(actual, JsonDefaults.Options), state);
    }

    private static async Task PrivateAsync(string serialized, ClusterBackupPrincipalCaptureState state)
    {
        await Assert.That(serialized.Contains(state.Credential, StringComparison.Ordinal)).IsFalse();
        await Assert.That(serialized.Contains(state.Principal.Id, StringComparison.Ordinal)).IsFalse();
    }
}

using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;
using P = KeyLoad.ClusterTests.Authorization.RowRevisionInferenceClusterProtocol;

namespace KeyLoad.ClusterTests.Authorization;

internal static class RowRevisionInferenceClusterRefusals
{
    internal static async Task SdkAsync(Result<CommitReceipt> actual, ErrorCode code, string detail, RowRevisionInferenceClusterState state)
    {
        await Assert.That(actual.IsFailed).IsTrue();
        await Assert.That(actual.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), actual.Problem);
        var json = JsonSerializer.Serialize(actual.Problem, JsonDefaults.Options);
        await PrivateAsync(json, state);
    }

    internal static async Task McpAsync(CallToolResult actual, ErrorCode code, RowRevisionInferenceClusterState state)
    {
        _ = (await McpCallerAssertions.ErrorAsync(actual, code, dispatched: true)).Value;
        var detail = code == ErrorCode.PermissionDenied ? P.SafeDenied : P.SafeOther;
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), JsonSerializer.Deserialize<Problem>(
            actual.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error).GetRawText(), JsonDefaults.Options));
        await PrivateAsync(JsonSerializer.Serialize(actual), state);
    }

    private static async Task PrivateAsync(string json, RowRevisionInferenceClusterState state)
    {
        await Assert.That(json.Contains(state.Key, StringComparison.Ordinal)).IsFalse();
        await Assert.That(json.Contains(P.Canary, StringComparison.Ordinal)).IsFalse();
    }
}

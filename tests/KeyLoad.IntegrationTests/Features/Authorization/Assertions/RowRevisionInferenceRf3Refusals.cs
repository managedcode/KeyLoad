using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;
using P = KeyLoad.IntegrationTests.Features.Authorization.RowRevisionInferenceRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RowRevisionInferenceRf3Refusals
{
    internal static async Task SdkAsync(Result<CommitReceipt> actual, ErrorCode code, string detail, RowRevisionInferenceRf3State state)
    {
        await Assert.That(actual.IsFailed).IsTrue();
        await Assert.That(actual.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(code, detail), actual.Problem);
        var json = JsonSerializer.Serialize(actual.Problem, JsonDefaults.Options);
        await PrivateAsync(json, state);
    }

    internal static async Task McpAsync(CallToolResult actual, ErrorCode code, RowRevisionInferenceRf3State state)
    {
        await McpCallerAssertions.ErrorAsync(actual, code, dispatched: true);
        var detail = code == ErrorCode.PermissionDenied ? P.SafeDenied : P.SafeOther;
        await SqlRf3Protocol.EqualAsync(Errors.Problem(code, detail), JsonSerializer.Deserialize<Problem>(
            actual.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error).GetRawText(), JsonDefaults.Options));
        await PrivateAsync(JsonSerializer.Serialize(actual), state);
    }

    private static async Task PrivateAsync(string json, RowRevisionInferenceRf3State state)
    {
        await Assert.That(json.Contains(state.Key, StringComparison.Ordinal)).IsFalse();
        await Assert.That(json.Contains(P.Canary, StringComparison.Ordinal)).IsFalse();
    }
}

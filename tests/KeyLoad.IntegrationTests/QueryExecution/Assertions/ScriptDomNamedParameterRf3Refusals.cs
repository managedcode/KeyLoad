using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;
using P = KeyLoad.IntegrationTests.Features.QueryExecution.ScriptDomNamedParameterRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ScriptDomNamedParameterRf3Refusals
{
    internal static async Task QueryAsync(Result<QueryPage> result)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.Validation, P.InvalidQuery), result.Problem);
    }
    internal static async Task UnifiedAsync(Result<JsonElement> result, string detail)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.Validation, detail), result.Problem);
    }
    internal static async Task McpAsync(CallToolResult reply, string credential, bool dispatched)
    {
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.Validation, dispatched);
        await Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(
            Errors.Problem(ErrorCode.Validation, P.SafeDetail), JsonDefaults.Options),
            reply.StructuredContent!.Value.GetProperty(P.Error))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, SqlParserAdmissionRf3State.Secret);
    }
}

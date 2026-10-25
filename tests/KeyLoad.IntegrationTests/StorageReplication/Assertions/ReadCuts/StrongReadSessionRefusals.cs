using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using static KeyLoad.IntegrationTests.StorageReplication.StrongReadSessionProtocol;

namespace KeyLoad.IntegrationTests.StorageReplication;

internal static class StrongReadSessionRefusals
{
    internal static async Task CallAsync(RequestCqrsClusterCallers reader, StrongReadSessionState state,
        EntityRef reference, CommitToken minimum, ErrorCode code, string detail, StrongReadSessionRoute route,
        CancellationToken token)
    {
        var request = new GetDocumentRequest(reference, minimum);
        var call = SqlClusterProtocol.Call(state.Partition, McpCallerTools.DocumentsGet, request);
        var expected = Errors.Problem(code, detail);
        if (route == StrongReadSessionRoute.SdkGet)
        {
            var actual = await reader.Sdk.GetAsync(reference, minimum, token);
            await Assert.That(actual.IsFailed).IsTrue();
            await Assert.That(actual.Value).IsNull();
            await SqlClusterProtocol.EqualAsync(expected, actual.Problem);
            return;
        }
        if (route == StrongReadSessionRoute.SdkCall)
        {
            var actual = await reader.Sdk.ExecuteSqlAsync(call, token);
            await Assert.That(actual.IsFailed).IsTrue();
            await Assert.That(actual.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
            await SqlClusterProtocol.EqualAsync(expected, actual.Problem);
            return;
        }
        var reply = route == StrongReadSessionRoute.McpGet
            ? await reader.Mcp.CallAsync(McpCallerTools.DocumentsGet, request, token)
            : await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, call, token);
        _ = (await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true)).Value;
        var golden = JsonSerializer.SerializeToElement(expected, JsonDefaults.Options);
        await Assert.That(JsonElement.DeepEquals(golden, reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Identity.Secret, SentinelJson);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Identity.Secret, TargetJson);
    }
    internal static (CommitToken Token, string Detail)[] Invalid(CommitToken original) =>
        [(original with { Incarnation = Guid.NewGuid() }, WrongIncarnation),
         (original with { AtomicPartitionId = Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat) }, WrongScope),
         (original with { Position = InvalidCut }, InvalidPosition),
         (original with { Position = long.MaxValue }, FuturePosition)];
}

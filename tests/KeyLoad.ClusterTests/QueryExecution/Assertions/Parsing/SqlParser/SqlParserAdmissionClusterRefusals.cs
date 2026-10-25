using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using ModelContextProtocol.Protocol;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlParserAdmissionClusterRefusals
{
    private const string PrivateParameter = "private";
    internal static async Task QueryAsync(RequestCqrsClusterCallers reader, SqlParserAdmissionClusterState state,
        string sql, ErrorCode code, string detail, bool unifiedDispatched, CancellationToken token)
    {
        var parameters = new Dictionary<string, JsonElement> { [PrivateParameter] = JsonSerializer.SerializeToElement(SqlParserAdmissionClusterState.Secret) };
        var request = new QueryRequest(state.Partition, sql, parameters, AllowFullScan: true);
        var sdk = await reader.Sdk.QueryAsync(request, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), sdk.Problem);
        await McpAsync(await reader.Mcp.CallAsync(McpCallerTools.QueryExecute, request, token), state.Original.Identity.Secret, code, true);
        var envelope = new SqlOperationRequest(state.Partition, sql, parameters, AllowFullScan: true);
        var unified = await reader.Sdk.ExecuteSqlAsync(envelope, token);
        await Assert.That(unified.IsFailed).IsTrue();
        await Assert.That(unified.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), unified.Problem);
        await McpAsync(await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, envelope, token), state.Original.Identity.Secret, code, unifiedDispatched);
    }
    internal static async Task InsertAsync(RequestCqrsClusterCallers root, SqlParserAdmissionClusterState state, CancellationToken token)
    {
        var request = new SqlOperationRequest(state.Partition,
            "INSERT INTO [parser-typed] (id,title,amount) VALUES ('refused','private',-1 COLLATE Latin1_General_100_BIN2)", CommandId: Guid.NewGuid());
        var sdk = await root.Sdk.ExecuteSqlAsync(request, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.UnsupportedCapability,
            "The unified SQL version, statement or target operation is unsupported."), sdk.Problem);
        await McpAsync(await root.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token), state.RootCredential, ErrorCode.UnsupportedCapability, false);
        var absent = new EntityRef(state.Partition, SqlParserAdmissionClusterState.Table, "refused");
        await SqlClusterProtocol.EqualAsync(new DocumentReadResult(null), await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(absent, token)));
    }
    private static async Task McpAsync(CallToolResult reply, string credential, ErrorCode code, bool dispatched)
    {
        _ = (await McpCallerAssertions.ErrorAsync(reply, code, dispatched)).Value;
        var detail = code == ErrorCode.PermissionDenied ? "The principal cannot perform this operation in this scope."
            : "The database operation could not be completed.";
        await Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(Errors.Problem(code, detail), JsonDefaults.Options),
            reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, SqlParserAdmissionClusterState.Secret);
    }
}

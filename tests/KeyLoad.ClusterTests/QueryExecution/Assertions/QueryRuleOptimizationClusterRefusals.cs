using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.Query;
using ModelContextProtocol.Protocol;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class QueryRuleOptimizationClusterRefusals
{
    private const string FieldUseDenied = "The query requires a protected field-use grant.";
    private const string ScanConsentDenied = "The query requires an index or explicit full-scan permission.";
    private const string SafePermissionDenied = "The authenticated principal is not permitted to perform this operation.";
    private const string SafeUnavailable = "The database operation could not be completed.";

    internal static async Task CheckAsync(RequestCqrsClusterCallers reader, AstQueryRequest ast, QueryRequest sql,
        int route, bool denied, CancellationToken token)
    {
        var code = denied ? ErrorCode.PermissionDenied : ErrorCode.UnsupportedCapability;
        var detail = denied ? FieldUseDenied
            : ScanConsentDenied;
        var safe = denied ? SafePermissionDenied
            : SafeUnavailable;
        if (route is QueryRuleOptimizationClusterData.SqlSdkRoute or QueryRuleOptimizationClusterData.AstSdkRoute)
        {
            var result = route == QueryRuleOptimizationClusterData.SqlSdkRoute ? await reader.Sdk.QueryAsync(sql, token) : await reader.Sdk.QueryAstAsync(ast, token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), result.Problem);
        }
        else if (route == QueryRuleOptimizationClusterData.UnifiedSdkRoute)
        {
            var result = await reader.Sdk.ExecuteSqlAsync(new(sql.Partition, sql.Sql, sql.Parameters, sql.AllowFullScan, sql.Cursor), token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
            await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), result.Problem);
        }
        else
        {
            var reply = route switch
            {
                QueryRuleOptimizationClusterData.SqlMcpRoute => await reader.Mcp.CallAsync(McpCallerTools.QueryExecute, sql, token),
                QueryRuleOptimizationClusterData.UnifiedMcpRoute => await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName,
                    new SqlOperationRequest(sql.Partition, sql.Sql, sql.Parameters, sql.AllowFullScan, sql.Cursor), token),
                _ => await reader.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token)
            };
            await ProblemAsync(reply, code, safe);
        }
    }
    private static async Task ProblemAsync(CallToolResult reply, ErrorCode code, string detail)
    {
        _ = (await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true)).Value;
        await Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(Errors.Problem(code, detail), JsonDefaults.Options),
            reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error))).IsTrue();
    }
}

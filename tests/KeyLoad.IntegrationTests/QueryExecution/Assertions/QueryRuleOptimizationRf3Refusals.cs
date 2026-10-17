using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryRuleOptimizationRf3Refusals
{
    private const string FieldUseDenied = "The query requires a protected field-use grant.";
    private const string ScanConsentDenied = "The query requires an index or explicit full-scan permission.";
    private const string SafePermissionDenied = "The authenticated principal is not permitted to perform this operation.";
    private const string SafeUnavailable = "The database operation could not be completed.";

    internal static async Task CheckAsync(RequestCqrsRf3Callers reader, AstQueryRequest ast, QueryRequest sql,
        int route, bool denied, CancellationToken token)
    {
        var code = denied ? ErrorCode.PermissionDenied : ErrorCode.UnsupportedCapability;
        var detail = denied ? FieldUseDenied
            : ScanConsentDenied;
        var safe = denied ? SafePermissionDenied
            : SafeUnavailable;
        if (route is QueryRuleOptimizationRf3Data.SqlSdkRoute or QueryRuleOptimizationRf3Data.AstSdkRoute)
        {
            var result = route == QueryRuleOptimizationRf3Data.SqlSdkRoute ? await reader.Sdk.QueryAsync(sql, token) : await reader.Sdk.QueryAstAsync(ast, token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await SqlRf3Protocol.EqualAsync(Errors.Problem(code, detail), result.Problem);
        }
        else if (route == QueryRuleOptimizationRf3Data.UnifiedSdkRoute)
        {
            var result = await reader.Sdk.ExecuteSqlAsync(new(sql.Partition, sql.Sql, sql.Parameters, sql.AllowFullScan, sql.Cursor), token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
            await SqlRf3Protocol.EqualAsync(Errors.Problem(code, detail), result.Problem);
        }
        else
        {
            var reply = route switch
            {
                QueryRuleOptimizationRf3Data.SqlMcpRoute => await reader.Mcp.CallAsync(McpCallerTools.QueryExecute, sql, token),
                QueryRuleOptimizationRf3Data.UnifiedMcpRoute => await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName,
                    new SqlOperationRequest(sql.Partition, sql.Sql, sql.Parameters, sql.AllowFullScan, sql.Cursor), token),
                _ => await reader.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token)
            };
            await ProblemAsync(reply, code, safe);
        }
    }
    private static async Task ProblemAsync(CallToolResult reply, ErrorCode code, string detail)
    {
        await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true);
        await Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(Errors.Problem(code, detail), JsonDefaults.Options),
            reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error))).IsTrue();
    }
}

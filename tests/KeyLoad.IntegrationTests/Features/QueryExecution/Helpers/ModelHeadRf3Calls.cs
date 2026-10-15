using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ModelHeadRf3Calls
{
    internal static async Task<QueryPage> ReadAsync(RequestCqrsRf3Callers reader, ModelHeadRf3State state,
        bool queue, int route, CancellationToken token)
    {
        var sql = ModelHeadRf3Requests.Sql(state, queue);
        var ast = ModelHeadRf3Requests.Ast(state, queue);
        return route switch
        {
            0 => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAsync(sql, token)),
            1 => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(ast, token)),
            2 => (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(McpCallerTools.QueryExecute, sql, token))).Value,
            3 => (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token))).Value,
            4 => await SqlRf3Protocol.SdkAsync<QueryPage>(reader.Sdk, new(state.Partition, sql.Sql, AllowFullScan: true), token),
            _ => await SqlRf3Protocol.McpAsync<QueryPage>(reader.Mcp, new(state.Partition, sql.Sql, AllowFullScan: true), token)
        };
    }
}

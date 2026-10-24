using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class PolicyEpochPublicCalls
{
    internal static async Task<QueryPage> ReadAsync(RequestCqrsClusterCallers callers, QueryRequest request,
        int route, CancellationToken token)
    {
        if (route == PolicyEpochPublicProtocol.Sdk)
        {
            return await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.QueryAsync(request, token));
        }
        if (route == PolicyEpochPublicProtocol.Mcp)
        {
            return (await McpCallerAssertions.SuccessAsync<QueryPage>(await callers.Mcp.CallAsync(McpCallerTools.QueryExecute, request, token))).Value;
        }
        if (route == PolicyEpochPublicProtocol.SdkAst)
        {
            return await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.QueryAstAsync(
            PolicyEpochPublicProtocol.Ast(request.Partition, request.Cursor), token));
        }
        if (route == PolicyEpochPublicProtocol.McpAst)
        {
            return (await McpCallerAssertions.SuccessAsync<QueryPage>(await callers.Mcp.CallAsync(McpCallerTools.QueryAst,
            PolicyEpochPublicProtocol.Ast(request.Partition, request.Cursor), token))).Value;
        }
        var sql = SqlClusterProtocol.Call(request.Partition, McpCallerTools.QueryExecute, request);
        return route == PolicyEpochPublicProtocol.Q1Sdk
            ? await SqlClusterProtocol.SdkAsync<QueryPage>(callers.Sdk, sql, token)
            : await SqlClusterProtocol.McpAsync<QueryPage>(callers.Mcp, sql, token);
    }
}

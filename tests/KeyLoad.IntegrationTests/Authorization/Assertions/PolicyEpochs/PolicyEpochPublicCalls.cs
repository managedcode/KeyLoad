using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class PolicyEpochPublicCalls
{
    internal static async Task<QueryPage> ReadAsync(RequestCqrsRf3Callers callers, QueryRequest request,
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
        var sql = SqlRf3Protocol.Call(request.Partition, McpCallerTools.QueryExecute, request);
        return route == PolicyEpochPublicProtocol.Q1Sdk
            ? await SqlRf3Protocol.SdkAsync<QueryPage>(callers.Sdk, sql, token)
            : await SqlRf3Protocol.McpAsync<QueryPage>(callers.Mcp, sql, token);
    }
}

using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class PolicyEpochAuthorityRefusals
{
    internal static async Task AllAsync(RequestCqrsRf3Callers reader, PolicyEpochPublicState state,
        string[] cursors, CancellationToken token)
    {
        for (var route = 0; route < PolicyEpochPublicProtocol.RouteCount; route++)
        {
            var request = PolicyEpochPublicProtocol.Request(state.Scenario.Partition, cursors[route]);
            if (route is PolicyEpochPublicProtocol.Sdk or PolicyEpochPublicProtocol.SdkAst or PolicyEpochPublicProtocol.Q1Sdk)
            {
                await SdkAsync(reader, request, route, state.CurrentReader.Secret, token);
            }
            else
            {
                await OfficialAsync(reader, request, route, state.CurrentReader.Secret, token);
            }
        }
    }

    private static async Task SdkAsync(RequestCqrsRf3Callers reader, global::KeyLoad.QueryRequest request,
        int route, string credential, CancellationToken token)
    {
        if (route == PolicyEpochPublicProtocol.Sdk)
        {
            await PolicyEpochAuthorityProblem.SdkAsync(await reader.Sdk.QueryAsync(request, token), credential);
            return;
        }
        if (route == PolicyEpochPublicProtocol.SdkAst)
        {
            await PolicyEpochAuthorityProblem.SdkAsync(await reader.Sdk.QueryAstAsync(
                PolicyEpochPublicProtocol.Ast(request.Partition, request.Cursor), token), credential);
            return;
        }
        await PolicyEpochAuthorityProblem.SdkAsync(await reader.Sdk.ExecuteSqlAsync(
            SqlRf3Protocol.Call(request.Partition, McpCallerTools.QueryExecute, request), token), credential);
    }

    private static Task OfficialAsync(RequestCqrsRf3Callers reader, global::KeyLoad.QueryRequest request,
        int route, string credential, CancellationToken token)
    {
        if (route == PolicyEpochPublicProtocol.McpAst)
        {
            return PolicyEpochAuthorityProblem.OfficialAsync(() => reader.Mcp.CallAsync(McpCallerTools.QueryAst,
                PolicyEpochPublicProtocol.Ast(request.Partition, request.Cursor), token), credential);
        }
        if (route == PolicyEpochPublicProtocol.Mcp)
        {
            return PolicyEpochAuthorityProblem.OfficialAsync(() => reader.Mcp.CallAsync(McpCallerTools.QueryExecute,
                request, token), credential);
        }
        return PolicyEpochAuthorityProblem.OfficialAsync(() => reader.Mcp.CallAsync(SqlOperationProtocol.ToolName,
            SqlRf3Protocol.Call(request.Partition, McpCallerTools.QueryExecute, request), token), credential);
    }
}

using System.Text;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class PolicyEpochPublicRefusals
{
    internal static async Task AllAsync(ClusterFixture fixture, RequestCqrsClusterCallers reader, KeyLoadClient root,
        PolicyEpochPublicState state, ErrorCode code, CancellationToken token)
    {
        await PolicyEpochPublicAssertions.ImageAsync(root, state.Scenario, token);
        for (var route = 0; route < PolicyEpochPublicProtocol.RouteCount; route++)
        {
            var request = PolicyEpochPublicProtocol.Request(state.Scenario.Partition, state.OriginalCursors[route]);
            await RouteAsync(fixture, reader, request, route, code, state.CurrentReader.Secret, token);
            await PolicyEpochPublicAssertions.ImageAsync(root, state.Scenario, token);
        }
        await PolicyEpochPublicAssertions.ImageAsync(root, state.Scenario, token);
        await PolicyEpochPublicPolicy.ReplayAsync(root, state, token);
    }

    private static async Task RouteAsync(ClusterFixture fixture, RequestCqrsClusterCallers reader, QueryRequest request, int route,
        ErrorCode code, string credential, CancellationToken token)
    {
        var sql = SqlClusterProtocol.Call(request.Partition, McpCallerTools.QueryExecute, request);
        if (route == PolicyEpochPublicProtocol.Sdk)
        {
            await SdkAsync(await reader.Sdk.QueryAsync(request, token), code, credential);
            return;
        }
        if (route == PolicyEpochPublicProtocol.SdkAst)
        {
            await SdkAsync(await reader.Sdk.QueryAstAsync(PolicyEpochPublicProtocol.Ast(
            request.Partition, request.Cursor), token), code, credential);
            return;
        }
        if (route == PolicyEpochPublicProtocol.Q1Sdk)
        {
            await SdkAsync(await reader.Sdk.ExecuteSqlAsync(sql, token), code, credential);
            return;
        }
        if (code == ErrorCode.Unauthenticated)
        {
            await RefuseMcpAsync(fixture, reader, request, sql, route, credential, token);
            return;
        }
        var reply = route == PolicyEpochPublicProtocol.McpAst
            ? await reader.Mcp.CallAsync(McpCallerTools.QueryAst,
                PolicyEpochPublicProtocol.Ast(request.Partition, request.Cursor), token)
            : route == PolicyEpochPublicProtocol.Mcp
                ? await reader.Mcp.CallAsync(McpCallerTools.QueryExecute, request, token)
                : await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token);
        await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, ResourcePolicyUpdateClusterProtocol.SecretValue);
    }

    private static async Task RefuseMcpAsync(ClusterFixture fixture, RequestCqrsClusterCallers reader,
        QueryRequest request, SqlOperationRequest sql, int route, string credential, CancellationToken token)
    {
        var error = await Assert.ThrowsAsync<global::System.Net.Http.HttpRequestException>(
            () => route == PolicyEpochPublicProtocol.McpAst
                ? reader.Mcp.CallAsync(McpCallerTools.QueryAst,
                    PolicyEpochPublicProtocol.Ast(request.Partition, request.Cursor), token)
                : route == PolicyEpochPublicProtocol.Mcp
                    ? reader.Mcp.CallAsync(McpCallerTools.QueryExecute, request, token)
                    : reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token));
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.StatusCode).IsEqualTo(global::System.Net.HttpStatusCode.Unauthorized);
        await Assert.That(error.Message.Contains(credential, StringComparison.Ordinal)).IsFalse();
        await Assert.That(error.Message.Contains(ResourcePolicyUpdateClusterProtocol.SecretValue, StringComparison.Ordinal)).IsFalse();
        await KeyLoad.IntegrationTests.ClientApi.McpUnauthorizedProbe.VerifyAsync(
            fixture, McpCallerProtocol.Node2, credential, token);
    }

    private static async Task SdkAsync<T>(ManagedCode.Communication.Result<T> result, ErrorCode code, string credential)
    {
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
        if (result.Value is JsonElement value)
        {
            await Assert.That(value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        }
        else
        {
            await McpCallerAssertions.NoValueAsync(result);
        }
        var text = Encoding.UTF8.GetString(JsonDefaults.Serialize(result.Problem));
        await Assert.That(text.Contains(credential, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(ResourcePolicyUpdateClusterProtocol.SecretValue, StringComparison.Ordinal)).IsFalse();
    }
}

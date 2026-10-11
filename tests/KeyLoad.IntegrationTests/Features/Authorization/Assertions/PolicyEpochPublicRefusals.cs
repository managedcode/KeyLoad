using System.Text;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class PolicyEpochPublicRefusals
{
    internal static async Task AllAsync(RequestCqrsRf3Callers reader, KeyLoadClient root,
        PolicyEpochPublicState state, ErrorCode code, CancellationToken token)
    {
        await PolicyEpochPublicAssertions.ImageAsync(root, state.Scenario, token);
        for (var route = 0; route < PolicyEpochPublicProtocol.RouteCount; route++)
        {
            var request = PolicyEpochPublicProtocol.Request(state.Scenario.Partition, state.OriginalCursors[route]);
            await RouteAsync(reader, request, route, code, state.CurrentReader.Secret, token);
        }
        await PolicyEpochPublicAssertions.ImageAsync(root, state.Scenario, token);
        await PolicyEpochPublicPolicy.ReplayAsync(root, state, token);
    }

    private static async Task RouteAsync(RequestCqrsRf3Callers reader, QueryRequest request, int route,
        ErrorCode code, string credential, CancellationToken token)
    {
        var sql = SqlRf3Protocol.Call(request.Partition, McpCallerTools.QueryExecute, request);
        if (route == PolicyEpochPublicProtocol.Sdk)
        { await SdkAsync(await reader.Sdk.QueryAsync(request, token), code, credential); return; }
        if (route == PolicyEpochPublicProtocol.Q1Sdk)
        { await SdkAsync(await reader.Sdk.ExecuteSqlAsync(sql, token), code, credential); return; }
        var reply = route == PolicyEpochPublicProtocol.Mcp
            ? await reader.Mcp.CallAsync(McpCallerTools.QueryExecute, request, token)
            : await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token);
        await McpCallerAssertions.ErrorAsync(reply, code, dispatched: code != ErrorCode.Unauthenticated);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, ResourcePolicyUpdateRf3Protocol.SecretValue);
    }

    private static async Task SdkAsync<T>(ManagedCode.Communication.Result<T> result, ErrorCode code, string credential)
    {
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
        if (result.Value is JsonElement value)
        { await Assert.That(value.ValueKind).IsEqualTo(JsonValueKind.Undefined); }
        else
        { await Assert.That(result.Value).IsNull(); }
        var text = Encoding.UTF8.GetString(JsonDefaults.Serialize(result.Problem));
        await Assert.That(text.Contains(credential, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(ResourcePolicyUpdateRf3Protocol.SecretValue, StringComparison.Ordinal)).IsFalse();
    }
}

using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class TopicSqlClusterRoutes
{
    private const int Sdk = 0;
    private const int Mcp = 1;
    private const int Q1Sdk = 2;
    private const int Q1Mcp = 3;

    internal static async Task<QueryPage> QueryAsync(RequestCqrsClusterCallers callers, int route,
        TopicSqlClusterState state, bool ast, CancellationToken token)
    {
        var tool = ast ? McpCallerTools.QueryAst : McpCallerTools.QueryExecute;
        object request = ast ? state.Ast : state.Sql();
        return route switch
        {
            Sdk => ast ? await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.QueryAstAsync(state.Ast, token))
                : await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.QueryAsync(state.Sql(), token)),
            Mcp => (await McpCallerAssertions.SuccessAsync<QueryPage>(await callers.Mcp.CallAsync(tool, request, token))).Value,
            Q1Sdk => await SqlClusterProtocol.SdkAsync<QueryPage>(callers.Sdk, SqlClusterProtocol.Call(state.Partition, tool, request), token),
            Q1Mcp => await SqlClusterProtocol.McpAsync<QueryPage>(callers.Mcp, SqlClusterProtocol.Call(state.Partition, tool, request), token),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };
    }

    internal static async Task<EventSourcePage> NativeAsync(RequestCqrsClusterCallers callers, int route,
        TopicSqlClusterState state, CancellationToken token)
        => route switch
        {
            Sdk => await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadEventSourceAsync(state.Read, token)),
            Mcp => (await McpCallerAssertions.SuccessAsync<EventSourcePage>(await callers.Mcp.CallAsync(McpCallerTools.EventsRead, state.Read, token))).Value,
            Q1Sdk => await SqlClusterProtocol.SdkAsync<EventSourcePage>(callers.Sdk, SqlClusterProtocol.Call(state.Partition, McpCallerTools.EventsRead, state.Read), token),
            Q1Mcp => await SqlClusterProtocol.McpAsync<EventSourcePage>(callers.Mcp, SqlClusterProtocol.Call(state.Partition, McpCallerTools.EventsRead, state.Read), token),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };

    internal static async Task DeniedAsync(RequestCqrsClusterCallers callers, int route,
        TopicSqlClusterState state, QueryRequest request, ErrorCode code, CancellationToken token)
    {
        if (route == Sdk)
        {
            var result = await callers.Sdk.QueryAsync(request, token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
            await PrivacyAsync(result);
            return;
        }
        var envelope = SqlClusterProtocol.Call(state.Partition, McpCallerTools.QueryExecute, request);
        if (route == Q1Sdk)
        {
            var result = await callers.Sdk.ExecuteSqlAsync(envelope, token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
            await Assert.That(result.Value.ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Undefined);
            await PrivacyAsync(result);
            return;
        }
        object payload = route == Mcp ? request : envelope;
        var reply = await callers.Mcp.CallAsync(route == Mcp ? McpCallerTools.QueryExecute : SqlOperationProtocol.ToolName,
            payload, token);
        await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true);
        await PrivacyAsync(reply);
    }

    private static async Task PrivacyAsync<T>(T result)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(result, JsonDefaults.Options);
        await Assert.That(json).DoesNotContain(TopicSqlClusterProtocol.Secret);
        await Assert.That(json).DoesNotContain(TopicSqlClusterProtocol.HeaderSecret);
    }
}

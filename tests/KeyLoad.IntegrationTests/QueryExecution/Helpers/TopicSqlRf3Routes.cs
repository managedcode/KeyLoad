using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class TopicSqlRf3Routes
{
    private const int Sdk = 0;
    private const int Mcp = 1;
    private const int Q1Sdk = 2;
    private const int Q1Mcp = 3;

    internal static async Task<QueryPage> QueryAsync(RequestCqrsRf3Callers callers, int route,
        TopicSqlRf3State state, bool ast, CancellationToken token)
    {
        var tool = ast ? McpCallerTools.QueryAst : McpCallerTools.QueryExecute;
        object request = ast ? state.Ast : state.Sql();
        return route switch
        {
            Sdk => ast ? await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.QueryAstAsync(state.Ast, token))
                : await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.QueryAsync(state.Sql(), token)),
            Mcp => (await McpCallerAssertions.SuccessAsync<QueryPage>(await callers.Mcp.CallAsync(tool, request, token))).Value,
            Q1Sdk => await SqlRf3Protocol.SdkAsync<QueryPage>(callers.Sdk, SqlRf3Protocol.Call(state.Partition, tool, request), token),
            Q1Mcp => await SqlRf3Protocol.McpAsync<QueryPage>(callers.Mcp, SqlRf3Protocol.Call(state.Partition, tool, request), token),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };
    }

    internal static async Task<EventSourcePage> NativeAsync(RequestCqrsRf3Callers callers, int route,
        TopicSqlRf3State state, CancellationToken token)
        => route switch
        {
            Sdk => await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadEventSourceAsync(state.Read, token)),
            Mcp => (await McpCallerAssertions.SuccessAsync<EventSourcePage>(await callers.Mcp.CallAsync(McpCallerTools.EventsRead, state.Read, token))).Value,
            Q1Sdk => await SqlRf3Protocol.SdkAsync<EventSourcePage>(callers.Sdk, SqlRf3Protocol.Call(state.Partition, McpCallerTools.EventsRead, state.Read), token),
            Q1Mcp => await SqlRf3Protocol.McpAsync<EventSourcePage>(callers.Mcp, SqlRf3Protocol.Call(state.Partition, McpCallerTools.EventsRead, state.Read), token),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };

    internal static async Task DeniedAsync(RequestCqrsRf3Callers callers, int route,
        TopicSqlRf3State state, QueryRequest request, ErrorCode code, CancellationToken token)
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
        var envelope = SqlRf3Protocol.Call(state.Partition, McpCallerTools.QueryExecute, request);
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
        await Assert.That(json).DoesNotContain(TopicSqlRf3Protocol.Secret);
        await Assert.That(json).DoesNotContain(TopicSqlRf3Protocol.HeaderSecret);
    }
}

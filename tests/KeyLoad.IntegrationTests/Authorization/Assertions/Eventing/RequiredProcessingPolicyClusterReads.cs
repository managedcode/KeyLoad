using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RequiredProcessingPolicyClusterReads
{
    internal static async Task ProjectedAsync(RequestCqrsClusterCallers reader, RequiredProcessingPolicyClusterState state, CancellationToken token)
    {
        var expected = new MessageInspectionResult(RequiredProcessingPolicyClusterValues.Expected(state, "first", 0, true));
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.InspectAsync(new(state.Lane, "first"), token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<MessageInspectionResult>(await reader.Mcp.CallAsync(
            McpCallerTools.MessagesInspect, new InspectMessageRequest(state.Lane, "first"), token))).Value);
        var ast = new AstQueryRequest(state.Partition,
            new(RequiredProcessingPolicyClusterState.Queue, null, [new("*", "*")], null, [], 10,
                ModelSource: new(ModelQuerySourceKind.QueueMessages, RequiredProcessingPolicyClusterState.Queue)), AllowFullScan: true);
        var sql = new QueryRequest(state.Partition, "SELECT TOP (10) * FROM QUEUE_MESSAGES('" + RequiredProcessingPolicyClusterState.Queue + "')", AllowFullScan: true);
        await PageAsync(await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(ast, token)), state);
        await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token))).Value, state);
        await PageAsync(await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAsync(sql, token)), state);
        await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(McpCallerTools.QueryExecute, sql, token))).Value, state);
    }
    private static async Task PageAsync(QueryPage page, RequiredProcessingPolicyClusterState state)
    {
        const string json = """{"id":"first","state":"Leased","attempts":1,"stateVersion":2,"notBefore":null,"expiresAt":null,"payload":{"value":1},"headers":{"trace":"one"}}""";
        var fields = state.Header ? "/headers/secret" : "/payload/secret";
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        await SqlClusterProtocol.EqualAsync(new QueryPage([new("first", 2L, json, true, [fields])], null, page.CutPosition, "model-scan:queue-messages"), page);
    }
}

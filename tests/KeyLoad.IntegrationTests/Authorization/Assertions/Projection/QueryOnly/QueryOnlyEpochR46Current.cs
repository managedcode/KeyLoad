using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class QueryOnlyEpochR46Current
{
    internal static async Task RequireAsync(RequestCqrsClusterCallers root,
        RequestCqrsClusterCallers reader, QueryOnlyEpochR46State state, CancellationToken token)
    {
        var scenario = state.Original.Scenario;
        await Assert.That(scenario.DocumentRevision).IsEqualTo(1L);
        await PolicyEpochPublicAssertions.ImageAsync(root.Sdk, scenario, token);
        var page = (await McpCallerAssertions.SuccessAsync<QueryPage>(await root.Mcp.CallAsync(
            McpCallerTools.QueryExecute, PolicyEpochPublicProtocol.Complete(scenario.Partition), token))).Value;
        QueryRow[] rows = [new(ResourcePolicyUpdateClusterProtocol.DocumentId, 1L,
            ResourcePolicyUpdateClusterProtocol.SeedJson, RedactedFields: []),
            new(PolicyEpochPublicProtocol.SecondId, 1L, PolicyEpochPublicProtocol.SecondJson, RedactedFields: [])];
        await SqlClusterProtocol.EqualAsync(rows, page.Rows.ToArray());
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(PolicyEpochPublicProtocol.Scan);
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        await DocumentsAsync(root, scenario, token);
        await DocumentsAsync(reader, scenario, token);
        await SqlClusterProtocol.EqualAsync(state.Outbox, await OutboxAsync(root, scenario, token));
    }

    internal static async Task<OutboxStatus> OutboxAsync(RequestCqrsClusterCallers root,
        ResourcePolicyUpdateClusterScenario scenario, CancellationToken token)
    {
        var result = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(scenario.Partition, token));
        await SqlClusterProtocol.EqualAsync(result, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await root.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(scenario.Partition), token))).Value);
        return result;
    }

    private static async Task DocumentsAsync(RequestCqrsClusterCallers caller,
        ResourcePolicyUpdateClusterScenario scenario, CancellationToken token)
    {
        foreach (var item in new[] { (ResourcePolicyUpdateClusterProtocol.DocumentId, ResourcePolicyUpdateClusterProtocol.SeedJson),
            (PolicyEpochPublicProtocol.SecondId, PolicyEpochPublicProtocol.SecondJson) })
        {
            var reference = new EntityRef(scenario.Partition, ResourcePolicyUpdateClusterProtocol.Collection, item.Item1);
            var expected = new DocumentResult(reference, 1L, item.Item2, false, []);
            await SqlClusterProtocol.EqualAsync(expected, await PublicReadAssertions.DocumentAsync(
                await caller.Sdk.GetAsync(reference, token)));
            await SqlClusterProtocol.EqualAsync(expected, (await PublicReadAssertions.DocumentAsync(
                await caller.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value);
        }
    }
}

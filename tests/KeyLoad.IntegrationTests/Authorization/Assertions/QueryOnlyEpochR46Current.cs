using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class QueryOnlyEpochR46Current
{
    internal static async Task RequireAsync(RequestCqrsRf3Callers root,
        RequestCqrsRf3Callers reader, QueryOnlyEpochR46State state, CancellationToken token)
    {
        var scenario = state.Original.Scenario;
        await Assert.That(scenario.DocumentRevision).IsEqualTo(1L);
        await PolicyEpochPublicAssertions.ImageAsync(root.Sdk, scenario, token);
        var page = (await McpCallerAssertions.SuccessAsync<QueryPage>(await root.Mcp.CallAsync(
            McpCallerTools.QueryExecute, PolicyEpochPublicProtocol.Complete(scenario.Partition), token))).Value;
        QueryRow[] rows = [new(ResourcePolicyUpdateRf3Protocol.DocumentId, 1L,
            ResourcePolicyUpdateRf3Protocol.SeedJson, RedactedFields: []),
            new(PolicyEpochPublicProtocol.SecondId, 1L, PolicyEpochPublicProtocol.SecondJson, RedactedFields: [])];
        await SqlRf3Protocol.EqualAsync(rows, page.Rows.ToArray());
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(PolicyEpochPublicProtocol.Scan);
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        await DocumentsAsync(root, scenario, token);
        await DocumentsAsync(reader, scenario, token);
        await SqlRf3Protocol.EqualAsync(state.Outbox, await OutboxAsync(root, scenario, token));
    }

    internal static async Task<OutboxStatus> OutboxAsync(RequestCqrsRf3Callers root,
        ResourcePolicyUpdateRf3Scenario scenario, CancellationToken token)
    {
        var result = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(scenario.Partition, token));
        await SqlRf3Protocol.EqualAsync(result, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await root.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(scenario.Partition), token))).Value);
        return result;
    }

    private static async Task DocumentsAsync(RequestCqrsRf3Callers caller,
        ResourcePolicyUpdateRf3Scenario scenario, CancellationToken token)
    {
        foreach (var item in new[] { (ResourcePolicyUpdateRf3Protocol.DocumentId, ResourcePolicyUpdateRf3Protocol.SeedJson),
            (PolicyEpochPublicProtocol.SecondId, PolicyEpochPublicProtocol.SecondJson) })
        {
            var reference = new EntityRef(scenario.Partition, ResourcePolicyUpdateRf3Protocol.Collection, item.Item1);
            var expected = new DocumentResult(reference, 1L, item.Item2, false, []);
            await SqlRf3Protocol.EqualAsync(expected, await PublicReadAssertions.DocumentAsync(
                await caller.Sdk.GetAsync(reference, token)));
            await SqlRf3Protocol.EqualAsync(expected, (await PublicReadAssertions.DocumentAsync(
                await caller.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value);
        }
    }
}

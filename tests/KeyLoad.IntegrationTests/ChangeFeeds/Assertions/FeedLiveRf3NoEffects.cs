using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class FeedLiveRf3NoEffects
{
    internal static async Task RequireAsync(RequestCqrsRf3Callers administrator,
        FeedLiveRf3Scenario scenario, QueryRow[] expected, long tail, Func<Task> refused, CancellationToken token)
    {
        var endpoint = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        await EndpointAsync(endpoint, endpoint);
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.QueryAstAsync(scenario.Query, token));
        var head = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        await LiteralAsync(before, head, expected, tail);
        await refused();
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.QueryAstAsync(scenario.Query, token));
        var retained = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var current = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        await EndpointAsync(current, endpoint);
        await LiteralAsync(after, retained, expected, tail);
        await Assert.That(after.CutPosition).IsGreaterThanOrEqualTo(before.CutPosition);
        await SqlRf3Protocol.EqualAsync(before.Rows, after.Rows);
        await Assert.That(after.Cursor).IsEqualTo(before.Cursor);
        await Assert.That(after.AccessPath).IsEqualTo(before.AccessPath);
        await SqlRf3Protocol.EqualAsync(head, retained);
    }

    internal static QueryRow[] Rows(long revision, string? json)
    {
        var hidden = new QueryRow(FeedLiveRf3Protocol.Hidden, FeedLiveRf3Protocol.FirstRevision,
            FeedLiveRf3Protocol.FirstJson, false, []);
        return json is null ? [hidden] : [new(FeedLiveRf3Protocol.First, revision, json, false, []), hidden];
    }

    private static async Task LiteralAsync(QueryPage page, OutboxStatus outbox, QueryRow[] expected, long tail)
    {
        await SqlRf3Protocol.EqualAsync(expected, page.Rows.ToArray());
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo("bounded-full-scan");
        await Assert.That(outbox.Head.Tail).IsEqualTo(tail);
        await Assert.That(outbox.Head.FirstAvailable).IsEqualTo(FeedLiveRf3Protocol.FirstSequence);
        await Assert.That(outbox.Head.StoredRecords).IsEqualTo(tail);
        await Assert.That(outbox.Head.StoredBytes).IsGreaterThan(0L);
        await Assert.That(outbox.Consumers.IsEmpty).IsTrue();
    }

    private static async Task EndpointAsync(NodeStatus actual, NodeStatus endpoint)
    {
        await Assert.That(actual.NodeId).IsEqualTo(endpoint.NodeId);
        await Assert.That(actual.ProcessId).IsEqualTo(endpoint.ProcessId);
        await Assert.That(actual.Incarnation).IsEqualTo(endpoint.Incarnation);
        await Assert.That(actual.RoutingReady).IsTrue();
        await Assert.That(actual.Voters).IsEqualTo(RequestCqrsRf3Protocol.NodeCount);
        await Assert.That(actual.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(actual.Applied).IsGreaterThanOrEqualTo(endpoint.Applied);
        await Assert.That(actual.ReadGeneration).IsGreaterThanOrEqualTo(endpoint.ReadGeneration);
    }
}

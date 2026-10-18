using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LiveRepairedClusterAssertions
{
    internal static QueryRow Row => new(FeedLiveClusterProtocol.First, FeedLiveClusterProtocol.RepairedRevision,
        FeedLiveClusterProtocol.FirstProjected, true, [FeedLiveClusterProtocol.SecretField]);

    internal static async Task DeltaAsync(RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers reader, FeedLiveClusterScenario scenario,
        ReadLiveQueryRequest request, CommitReceipt receipt, CancellationToken token)
    {
        var observeCut = await LiveQueryCutClusterAssertions.CreateAsync(administrator, reader, scenario, receipt,
            [Row], () => LiveTailClusterAssertions.QueryAsync(reader, scenario, token), token);
        var before = await observeCut();
        var page = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(request, token));
        await LiveQueryCutClusterAssertions.BoundsAsync(before, page.CutPosition, await observeCut());
        await SqlClusterProtocol.EqualAsync(new[] { new LiveQueryChange(FeedLiveClusterProtocol.RepairedSequence,
            receipt.Token, LiveQueryChangeKind.Upsert, scenario.Reference,
            FeedLiveClusterProtocol.RepairedRevision, Row) }, page.Changes.ToArray());
        await Assert.That(page.ThroughSequence).IsEqualTo(FeedLiveClusterProtocol.RepairedSequence);
        await Assert.That(page.HasMore).IsFalse();

        await LiveTailClusterAssertions.AllPathsAsync(administrator, reader, scenario, request, page, receipt, token);
        var ordinary = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(scenario.Query, token));
        await SqlClusterProtocol.EqualAsync(new[] { Row }, ordinary.Rows.ToArray());
        await Assert.That(ordinary.Cursor).IsNull();
        await observeCut();
    }

    internal static async Task FreshAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, FeedLiveClusterScenario scenario, CommitReceipt receipt, CancellationToken token)
    {
        var start = new StartLiveQueryRequest(scenario.Query);
        var sql = SqlClusterProtocol.Call(scenario.Partition, FeedLiveClusterProtocol.LiveStart, start);
        var querySql = SqlClusterProtocol.Call(scenario.Partition, LiveQueryCutClusterAssertions.QueryTool, scenario.Query);
        await RouteAsync(administrator, reader, scenario, receipt,
            async () => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.StartLiveQueryAsync(start, token)),
            async next => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(next, token)),
            () => LiveTailClusterAssertions.QueryAsync(reader, scenario, token), token);
        await RouteAsync(administrator, reader, scenario, receipt, async () =>
        {
            var reply = await reader.Mcp.CallAsync(FeedLiveClusterProtocol.LiveStart, start, token);
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, scenario.Identity.Secret, FeedLiveClusterProtocol.Secret);
            return (await McpCallerAssertions.SuccessAsync<LiveQuerySnapshot>(reply)).Value;
        }, async next => (await McpCallerAssertions.SuccessAsync<LiveQueryPage>(
            await reader.Mcp.CallAsync(FeedLiveClusterProtocol.LiveRead, next, token))).Value,
            async () => (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(
                LiveQueryCutClusterAssertions.QueryTool, scenario.Query, token))).Value, token);
        await RouteAsync(administrator, reader, scenario, receipt,
            () => SqlClusterProtocol.SdkAsync<LiveQuerySnapshot>(reader.Sdk, sql, token),
            next => SqlClusterProtocol.SdkAsync<LiveQueryPage>(reader.Sdk,
                SqlClusterProtocol.Call(scenario.Partition, FeedLiveClusterProtocol.LiveRead, next), token),
            () => SqlClusterProtocol.SdkAsync<QueryPage>(reader.Sdk, querySql, token), token);
        await RouteAsync(administrator, reader, scenario, receipt,
            () => SqlClusterProtocol.McpAsync<LiveQuerySnapshot>(reader.Mcp, sql, token),
            next => SqlClusterProtocol.McpAsync<LiveQueryPage>(reader.Mcp,
                SqlClusterProtocol.Call(scenario.Partition, FeedLiveClusterProtocol.LiveRead, next), token),
            () => SqlClusterProtocol.McpAsync<QueryPage>(reader.Mcp, querySql, token), token);
    }

    private static async Task RouteAsync(RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers reader,
        FeedLiveClusterScenario scenario, CommitReceipt receipt, Func<Task<LiveQuerySnapshot>> start,
        Func<ReadLiveQueryRequest, Task<LiveQueryPage>> read, Func<Task<QueryPage>> query, CancellationToken token)
    {
        var observeCut = await LiveQueryCutClusterAssertions.CreateAsync(administrator, reader, scenario, receipt,
            [Row], query, token);
        var before = await observeCut();
        var snapshot = await start();
        await LiveQueryCutClusterAssertions.BoundsAsync(before, snapshot.CutPosition, await observeCut());
        await SqlClusterProtocol.EqualAsync(new[] { Row }, snapshot.Rows.ToArray());
        await Assert.That(snapshot.ThroughSequence).IsEqualTo(FeedLiveClusterProtocol.RepairedSequence);
        await Assert.That(string.IsNullOrEmpty(snapshot.Cursor)).IsFalse();
        var request = new ReadLiveQueryRequest(scenario.Query, snapshot.Cursor);
        await LivePageClusterAssertions.RequireAsync(new LiveQueryPage([], snapshot.Cursor,
            FeedLiveClusterProtocol.RepairedSequence, false, snapshot.CutPosition), request, observeCut, read);
    }
}

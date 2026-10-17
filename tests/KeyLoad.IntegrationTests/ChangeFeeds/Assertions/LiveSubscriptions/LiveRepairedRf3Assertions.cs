using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LiveRepairedRf3Assertions
{
    internal static QueryRow Row => new(FeedLiveRf3Protocol.First, FeedLiveRf3Protocol.RepairedRevision,
        FeedLiveRf3Protocol.FirstProjected, true, [FeedLiveRf3Protocol.SecretField]);

    internal static async Task DeltaAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario,
        ReadLiveQueryRequest request, CommitReceipt receipt, CancellationToken token)
    {
        var observeCut = await LiveQueryCutRf3Assertions.CreateAsync(administrator, reader, scenario, receipt,
            [Row], () => LiveTailRf3Assertions.QueryAsync(reader, scenario, token), token);
        var before = await observeCut();
        var page = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(request, token));
        await LiveQueryCutRf3Assertions.BoundsAsync(before, page.CutPosition, await observeCut());
        await SqlRf3Protocol.EqualAsync(new[] { new LiveQueryChange(FeedLiveRf3Protocol.RepairedSequence,
            receipt.Token, LiveQueryChangeKind.Upsert, scenario.Reference,
            FeedLiveRf3Protocol.RepairedRevision, Row) }, page.Changes.ToArray());
        await Assert.That(page.ThroughSequence).IsEqualTo(FeedLiveRf3Protocol.RepairedSequence);
        await Assert.That(page.HasMore).IsFalse();

        await LiveTailRf3Assertions.AllPathsAsync(administrator, reader, scenario, request, page, receipt, token);
        var ordinary = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(scenario.Query, token));
        await SqlRf3Protocol.EqualAsync(new[] { Row }, ordinary.Rows.ToArray());
        await Assert.That(ordinary.Cursor).IsNull();
        await observeCut();
    }

    internal static async Task FreshAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario, CommitReceipt receipt, CancellationToken token)
    {
        var start = new StartLiveQueryRequest(scenario.Query);
        var sql = SqlRf3Protocol.Call(scenario.Partition, FeedLiveRf3Protocol.LiveStart, start);
        var querySql = SqlRf3Protocol.Call(scenario.Partition, LiveQueryCutRf3Assertions.QueryTool, scenario.Query);
        await RouteAsync(administrator, reader, scenario, receipt,
            async () => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.StartLiveQueryAsync(start, token)),
            async next => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(next, token)),
            () => LiveTailRf3Assertions.QueryAsync(reader, scenario, token), token);
        await RouteAsync(administrator, reader, scenario, receipt, async () =>
        {
            var reply = await reader.Mcp.CallAsync(FeedLiveRf3Protocol.LiveStart, start, token);
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, scenario.Identity.Secret, FeedLiveRf3Protocol.Secret);
            return (await McpCallerAssertions.SuccessAsync<LiveQuerySnapshot>(reply)).Value;
        }, async next => (await McpCallerAssertions.SuccessAsync<LiveQueryPage>(
            await reader.Mcp.CallAsync(FeedLiveRf3Protocol.LiveRead, next, token))).Value,
            async () => (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(
                LiveQueryCutRf3Assertions.QueryTool, scenario.Query, token))).Value, token);
        await RouteAsync(administrator, reader, scenario, receipt,
            () => SqlRf3Protocol.SdkAsync<LiveQuerySnapshot>(reader.Sdk, sql, token),
            next => SqlRf3Protocol.SdkAsync<LiveQueryPage>(reader.Sdk,
                SqlRf3Protocol.Call(scenario.Partition, FeedLiveRf3Protocol.LiveRead, next), token),
            () => SqlRf3Protocol.SdkAsync<QueryPage>(reader.Sdk, querySql, token), token);
        await RouteAsync(administrator, reader, scenario, receipt,
            () => SqlRf3Protocol.McpAsync<LiveQuerySnapshot>(reader.Mcp, sql, token),
            next => SqlRf3Protocol.McpAsync<LiveQueryPage>(reader.Mcp,
                SqlRf3Protocol.Call(scenario.Partition, FeedLiveRf3Protocol.LiveRead, next), token),
            () => SqlRf3Protocol.McpAsync<QueryPage>(reader.Mcp, querySql, token), token);
    }

    private static async Task RouteAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader,
        FeedLiveRf3Scenario scenario, CommitReceipt receipt, Func<Task<LiveQuerySnapshot>> start,
        Func<ReadLiveQueryRequest, Task<LiveQueryPage>> read, Func<Task<QueryPage>> query, CancellationToken token)
    {
        var observeCut = await LiveQueryCutRf3Assertions.CreateAsync(administrator, reader, scenario, receipt,
            [Row], query, token);
        var before = await observeCut();
        var snapshot = await start();
        await LiveQueryCutRf3Assertions.BoundsAsync(before, snapshot.CutPosition, await observeCut());
        await SqlRf3Protocol.EqualAsync(new[] { Row }, snapshot.Rows.ToArray());
        await Assert.That(snapshot.ThroughSequence).IsEqualTo(FeedLiveRf3Protocol.RepairedSequence);
        await Assert.That(string.IsNullOrEmpty(snapshot.Cursor)).IsFalse();
        var request = new ReadLiveQueryRequest(scenario.Query, snapshot.Cursor);
        await LivePageRf3Assertions.RequireAsync(new LiveQueryPage([], snapshot.Cursor,
            FeedLiveRf3Protocol.RepairedSequence, false, snapshot.CutPosition), request, observeCut, read);
    }
}

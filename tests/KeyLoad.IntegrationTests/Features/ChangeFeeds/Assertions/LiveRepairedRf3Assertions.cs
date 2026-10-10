using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LiveRepairedRf3Assertions
{
    private static QueryRow Row => new(FeedLiveRf3Protocol.First, FeedLiveRf3Protocol.RepairedRevision,
        FeedLiveRf3Protocol.FirstProjected, true, [FeedLiveRf3Protocol.SecretField]);

    internal static async Task DeltaAsync(RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario,
        ReadLiveQueryRequest request, CommitReceipt receipt, CancellationToken token)
    {
        var page = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(request, token));
        await SqlRf3Protocol.EqualAsync(new[] { new LiveQueryChange(FeedLiveRf3Protocol.RepairedSequence,
            receipt.Token, LiveQueryChangeKind.Upsert, scenario.Reference,
            FeedLiveRf3Protocol.RepairedRevision, Row) }, page.Changes.ToArray());
        await Assert.That(page.ThroughSequence).IsEqualTo(FeedLiveRf3Protocol.RepairedSequence);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(receipt.Token.Position);
        await LiveTailRf3Assertions.AllPathsAsync(reader, scenario, request, page, token);
        var ordinary = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(scenario.Query, token));
        await SqlRf3Protocol.EqualAsync(new[] { Row }, ordinary.Rows.ToArray());
        await Assert.That(ordinary.Cursor).IsNull();
        await Assert.That(ordinary.CutPosition).IsGreaterThanOrEqualTo(receipt.Token.Position);
    }

    internal static async Task FreshAsync(RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario,
        CommitReceipt receipt, CancellationToken token)
    {
        var start = new StartLiveQueryRequest(scenario.Query);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.StartLiveQueryAsync(start, token));
        await FreshPageAsync(sdk, scenario, receipt,
            async next => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(next, token)));
        var reply = await reader.Mcp.CallAsync(FeedLiveRf3Protocol.LiveStart, start, token);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, scenario.Identity.Secret, FeedLiveRf3Protocol.Secret);
        var official = (await McpCallerAssertions.SuccessAsync<LiveQuerySnapshot>(reply)).Value;
        await FreshPageAsync(official, scenario, receipt,
            async next => (await McpCallerAssertions.SuccessAsync<LiveQueryPage>(
                await reader.Mcp.CallAsync(FeedLiveRf3Protocol.LiveRead, next, token))).Value);
        var sql = SqlRf3Protocol.Call(scenario.Partition, FeedLiveRf3Protocol.LiveStart, start);
        var sdkSql = await SqlRf3Protocol.SdkAsync<LiveQuerySnapshot>(reader.Sdk, sql, token);
        await FreshPageAsync(sdkSql, scenario, receipt, next => SqlRf3Protocol.SdkAsync<LiveQueryPage>(reader.Sdk,
            SqlRf3Protocol.Call(scenario.Partition, FeedLiveRf3Protocol.LiveRead, next), token));
        var mcpSql = await SqlRf3Protocol.McpAsync<LiveQuerySnapshot>(reader.Mcp, sql, token);
        await FreshPageAsync(mcpSql, scenario, receipt, next => SqlRf3Protocol.McpAsync<LiveQueryPage>(reader.Mcp,
            SqlRf3Protocol.Call(scenario.Partition, FeedLiveRf3Protocol.LiveRead, next), token));
    }

    private static async Task FreshPageAsync(LiveQuerySnapshot snapshot, FeedLiveRf3Scenario scenario,
        CommitReceipt receipt, Func<ReadLiveQueryRequest, Task<LiveQueryPage>> read)
    {
        await SqlRf3Protocol.EqualAsync(new[] { Row }, snapshot.Rows.ToArray());
        await Assert.That(snapshot.ThroughSequence).IsEqualTo(FeedLiveRf3Protocol.RepairedSequence);
        await Assert.That(snapshot.CutPosition).IsGreaterThanOrEqualTo(receipt.Token.Position);
        await Assert.That(string.IsNullOrEmpty(snapshot.Cursor)).IsFalse();
        var request = new ReadLiveQueryRequest(scenario.Query, snapshot.Cursor);
        var page = await read(request);
        await LivePageRf3Assertions.RequireAsync(page, new LiveQueryPage([], page.Cursor,
            FeedLiveRf3Protocol.RepairedSequence, false, snapshot.CutPosition), request, read);
    }
}

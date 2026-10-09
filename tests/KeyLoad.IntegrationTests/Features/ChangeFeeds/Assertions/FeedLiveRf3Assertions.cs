using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class FeedLiveRf3Assertions
{
    internal static async Task<ChangeFeedPage> FeedAsync(RequestCqrsRf3Callers reader,
        FeedLiveRf3Scenario scenario, ReadChangeFeedRequest request, CommitToken commit,
        long sequence, long revision, string json, bool updated, long tail, CancellationToken token)
    {
        var page = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadChangesAsync(request, token));
        await LiteralAsync(page, scenario, commit, sequence, revision, json, updated);
        var reply = await reader.Mcp.CallAsync(FeedLiveRf3Protocol.Feed, request, token);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, scenario.Identity.Secret, FeedLiveRf3Protocol.Secret);
        var official = (await McpCallerAssertions.SuccessAsync<ChangeFeedPage>(reply)).Value;
        await LiteralAsync(official, scenario, commit, sequence, revision, json, updated);
        await SqlRf3Protocol.EqualAsync(page.Changes, official.Changes);
        var sql = SqlRf3Protocol.Call(scenario.Partition, FeedLiveRf3Protocol.Feed, request);
        var sdkSql = await SqlRf3Protocol.SdkAsync<ChangeFeedPage>(reader.Sdk, sql, token);
        await LiteralAsync(sdkSql, scenario, commit, sequence, revision, json, updated);
        var mcpSql = await SqlRf3Protocol.McpAsync<ChangeFeedPage>(reader.Mcp, sql, token);
        await LiteralAsync(mcpSql, scenario, commit, sequence, revision, json, updated);
        await FeedPageRf3Assertions.RequireAsync(page, sequence, tail, commit, request,
            async next => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadChangesAsync(next, token)));
        await FeedPageRf3Assertions.RequireAsync(official, sequence, tail, commit, request,
            async next => (await McpCallerAssertions.SuccessAsync<ChangeFeedPage>(
                await reader.Mcp.CallAsync(FeedLiveRf3Protocol.Feed, next, token))).Value);
        await FeedPageRf3Assertions.RequireAsync(sdkSql,
            sequence, tail, commit, request, next => SqlRf3Protocol.SdkAsync<ChangeFeedPage>(reader.Sdk,
                SqlRf3Protocol.Call(scenario.Partition, FeedLiveRf3Protocol.Feed, next), token));
        await FeedPageRf3Assertions.RequireAsync(mcpSql,
            sequence, tail, commit, request, next => SqlRf3Protocol.McpAsync<ChangeFeedPage>(reader.Mcp,
                SqlRf3Protocol.Call(scenario.Partition, FeedLiveRf3Protocol.Feed, next), token));
        return page;
    }

    private static async Task LiteralAsync(ChangeFeedPage page, FeedLiveRf3Scenario scenario,
        CommitToken commit, long sequence, long revision, string json, bool updated)
    {
        await Assert.That(page.Changes.Length).IsEqualTo(FeedLiveRf3Protocol.PageLimit);
        var change = page.Changes.Single();
        var expected = new DocumentChange(sequence, commit, change.CommittedAt, scenario.Reference, revision, false,
            updated ? new(scenario.Reference, FeedLiveRf3Protocol.FirstRevision,
                FeedLiveRf3Protocol.FirstProjected, true, [FeedLiveRf3Protocol.SecretField]) : null,
            new(scenario.Reference, revision, json, true, [FeedLiveRf3Protocol.SecretField]));
        await SqlRf3Protocol.EqualAsync(expected, change);
        await Assert.That(change.CommittedAt).IsNotEqualTo(default(DateTimeOffset));
        await Assert.That(page.ThroughSequence).IsEqualTo(sequence);
        await Assert.That(page.FirstAvailable).IsEqualTo(FeedLiveRf3Protocol.FirstSequence);
        await Assert.That(page.Tail).IsGreaterThanOrEqualTo(sequence);
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(commit.Position);
        await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
    }

    internal static async Task DeniedAsync(RequestCqrsRf3Callers reader, ReadChangeFeedRequest request,
        ErrorCode expected, CancellationToken token)
    {
        var sdk = await reader.Sdk.ReadChangesAsync(request, token);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(expected.ToString());
        await Assert.That(sdk.Value).IsNull();
        await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(FeedLiveRf3Protocol.Feed, request, token),
            expected, dispatched: true);
        await LivePageRf3Assertions.DeniedSqlAsync(reader,
            SqlRf3Protocol.Call(request.Partition, FeedLiveRf3Protocol.Feed, request), expected, token);
    }
}

using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.ChangeFeeds;

internal static class FeedLiveClusterAssertions
{
    internal static async Task<ChangeFeedPage> FeedAsync(RequestCqrsClusterCallers reader,
        FeedLiveClusterScenario scenario, ReadChangeFeedRequest request, CommitToken commit,
        long sequence, long revision, string json, bool updated, long tail, CancellationToken token)
    {
        var page = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadChangesAsync(request, token));
        await LiteralAsync(page, scenario, commit, sequence, revision, json, updated);
        var reply = await reader.Mcp.CallAsync(FeedLiveClusterProtocol.Feed, request, token);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, scenario.Identity.Secret, FeedLiveClusterProtocol.Secret);
        var official = (await McpCallerAssertions.SuccessAsync<ChangeFeedPage>(reply)).Value;
        await LiteralAsync(official, scenario, commit, sequence, revision, json, updated);
        await SqlClusterProtocol.EqualAsync(page.Changes, official.Changes);
        var sql = SqlClusterProtocol.Call(scenario.Partition, FeedLiveClusterProtocol.Feed, request);
        var sdkSql = await SqlClusterProtocol.SdkAsync<ChangeFeedPage>(reader.Sdk, sql, token);
        await LiteralAsync(sdkSql, scenario, commit, sequence, revision, json, updated);
        var mcpSql = await SqlClusterProtocol.McpAsync<ChangeFeedPage>(reader.Mcp, sql, token);
        await LiteralAsync(mcpSql, scenario, commit, sequence, revision, json, updated);
        await FeedPageClusterAssertions.RequireAsync(page, sequence, tail, commit, request,
            async next => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadChangesAsync(next, token)));
        await FeedPageClusterAssertions.RequireAsync(official, sequence, tail, commit, request,
            async next => (await McpCallerAssertions.SuccessAsync<ChangeFeedPage>(
                await reader.Mcp.CallAsync(FeedLiveClusterProtocol.Feed, next, token))).Value);
        await FeedPageClusterAssertions.RequireAsync(sdkSql,
            sequence, tail, commit, request, next => SqlClusterProtocol.SdkAsync<ChangeFeedPage>(reader.Sdk,
                SqlClusterProtocol.Call(scenario.Partition, FeedLiveClusterProtocol.Feed, next), token));
        await FeedPageClusterAssertions.RequireAsync(mcpSql,
            sequence, tail, commit, request, next => SqlClusterProtocol.McpAsync<ChangeFeedPage>(reader.Mcp,
                SqlClusterProtocol.Call(scenario.Partition, FeedLiveClusterProtocol.Feed, next), token));
        return page;
    }

    private static async Task LiteralAsync(ChangeFeedPage page, FeedLiveClusterScenario scenario,
        CommitToken commit, long sequence, long revision, string json, bool updated)
    {
        await Assert.That(page.Changes.Length).IsEqualTo(FeedLiveClusterProtocol.PageLimit);
        var change = page.Changes.Single();
        var expected = new DocumentChange(sequence, commit, change.CommittedAt, scenario.Reference, revision, false,
            updated ? new(scenario.Reference, FeedLiveClusterProtocol.FirstRevision,
                FeedLiveClusterProtocol.FirstProjected, true, [FeedLiveClusterProtocol.SecretField]) : null,
            new(scenario.Reference, revision, json, true, [FeedLiveClusterProtocol.SecretField]));
        await SqlClusterProtocol.EqualAsync(expected, change);
        await Assert.That(change.CommittedAt).IsNotEqualTo(default(DateTimeOffset));
        await Assert.That(page.ThroughSequence).IsEqualTo(sequence);
        await Assert.That(page.FirstAvailable).IsEqualTo(FeedLiveClusterProtocol.FirstSequence);
        await Assert.That(page.Tail).IsGreaterThanOrEqualTo(sequence);
        await FeedLiveReadCoordinatesR49.RequireAsync(page.CutPosition, commit);
        await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
    }

    internal static async Task DeniedAsync(RequestCqrsClusterCallers reader, ReadChangeFeedRequest request,
        ErrorCode expected, CancellationToken token)
    {
        var sdk = await reader.Sdk.ReadChangesAsync(request, token);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(expected.ToString());
        await Assert.That(sdk.Value).IsNull();
        await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(FeedLiveClusterProtocol.Feed, request, token),
            expected, dispatched: true);
        await LivePageClusterAssertions.DeniedSqlAsync(reader,
            SqlClusterProtocol.Call(request.Partition, FeedLiveClusterProtocol.Feed, request), expected, token);
    }
}

using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.ChangeFeeds;

internal static class FeedGapClusterAssertions
{

    internal static async Task<ChangeFeedPage> AdvanceAsync(RequestCqrsClusterCallers reader,
        FeedLiveClusterScenario scenario, string cursor, CancellationToken token)
    {
        var request = scenario.Feed with
        {
            Cursor = cursor
        };
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadChangesAsync(request, token));
        var official = (await McpCallerAssertions.SuccessAsync<ChangeFeedPage>(await reader.Mcp.CallAsync(
            FeedLiveClusterProtocol.Feed, request, token))).Value;
        var sql = SqlClusterProtocol.Call(scenario.Partition, FeedLiveClusterProtocol.Feed, request);
        ChangeFeedPage[] pages = [sdk, official,
            await SqlClusterProtocol.SdkAsync<ChangeFeedPage>(reader.Sdk, sql, token),
            await SqlClusterProtocol.McpAsync<ChangeFeedPage>(reader.Mcp, sql, token)];
        foreach (var page in pages)
        {
            await Assert.That(page.Changes.IsEmpty).IsTrue();
            await Assert.That(page.ThroughSequence).IsEqualTo(FeedLiveClusterProtocol.HiddenSequence);
            await Assert.That(page.Tail).IsEqualTo(FeedLiveClusterProtocol.UpdatedSequence);
            await Assert.That(page.FirstAvailable).IsEqualTo(FeedLiveClusterProtocol.FirstSequence);
            await Assert.That(page.HasMore).IsTrue();
            await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
        }
        return sdk;
    }
}

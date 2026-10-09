using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class FeedGapRf3Assertions
{

    internal static async Task<ChangeFeedPage> AdvanceAsync(RequestCqrsRf3Callers reader,
        FeedLiveRf3Scenario scenario, string cursor, CancellationToken token)
    {
        var request = scenario.Feed with { Cursor = cursor };
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadChangesAsync(request, token));
        var official = (await McpCallerAssertions.SuccessAsync<ChangeFeedPage>(await reader.Mcp.CallAsync(
            FeedLiveRf3Protocol.Feed, request, token))).Value;
        var sql = SqlRf3Protocol.Call(scenario.Partition, FeedLiveRf3Protocol.Feed, request);
        ChangeFeedPage[] pages = [sdk, official,
            await SqlRf3Protocol.SdkAsync<ChangeFeedPage>(reader.Sdk, sql, token),
            await SqlRf3Protocol.McpAsync<ChangeFeedPage>(reader.Mcp, sql, token)];
        foreach (var page in pages)
        {
            await Assert.That(page.Changes.IsEmpty).IsTrue();
            await Assert.That(page.ThroughSequence).IsEqualTo(FeedLiveRf3Protocol.HiddenSequence);
            await Assert.That(page.Tail).IsEqualTo(FeedLiveRf3Protocol.UpdatedSequence);
            await Assert.That(page.FirstAvailable).IsEqualTo(FeedLiveRf3Protocol.FirstSequence);
            await Assert.That(page.HasMore).IsTrue();
            await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
        }
        return sdk;
    }
}

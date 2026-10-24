using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.ChangeFeeds;

internal static class CapturedPolicyClusterPages
{
    internal static async Task<(ReadChangeFeedRequest Feed, ReadLiveQueryRequest Live)> CaptureAsync(
        RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers reader,
        FeedLiveClusterScenario scenario, CancellationToken token)
    {
        LiveQuerySnapshot? snapshot = null;
        var start = new StartLiveQueryRequest(scenario.Query);
        await CapturedPolicyClusterCurrent.BracketAsync(administrator, reader, scenario, false, async () =>
        {
            snapshot = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.StartLiveQueryAsync(start, token));
            await SnapshotAsync(snapshot);
        }, token);
        await CapturedPolicyClusterCurrent.BracketAsync(administrator, reader, scenario, false, async () =>
            await SnapshotAsync((await McpCallerAssertions.SuccessAsync<LiveQuerySnapshot>(
                await reader.Mcp.CallAsync(McpCallerTools.QueryLiveStart, start, token))).Value), token);
        var feed = scenario.Feed with
        {
            Start = ChangeFeedStart.Now
        };
        ChangeFeedPage? caughtUp = null;
        await CapturedPolicyClusterCurrent.BracketAsync(administrator, reader, scenario, false, async () =>
        {
            caughtUp = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadChangesAsync(feed, token));
            await EmptyFeedAsync(caughtUp, FeedLiveClusterProtocol.HiddenSequence);
        }, token);
        await CapturedPolicyClusterCurrent.BracketAsync(administrator, reader, scenario, false, async () =>
            await EmptyFeedAsync((await McpCallerAssertions.SuccessAsync<ChangeFeedPage>(
                await reader.Mcp.CallAsync(McpCallerTools.ChangesRead, feed, token))).Value,
                FeedLiveClusterProtocol.HiddenSequence), token);
        return (feed with
        {
            Cursor = caughtUp!.Cursor
        }, new(scenario.Query, snapshot!.Cursor));
    }

    internal static async Task DeltaAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, FeedLiveClusterScenario scenario, ReadLiveQueryRequest request,
        CommitReceipt receipt, CancellationToken token)
    {
        var row = LiveTailClusterAssertions.UpdatedRow;
        var changes = new[] { new LiveQueryChange(FeedLiveClusterProtocol.UpdatedSequence, receipt.Token,
            LiveQueryChangeKind.Upsert, scenario.StoredReference, FeedLiveClusterProtocol.UpdatedRevision, row) };
        await RouteAsync(async value => await McpCallerAssertions.SdkSuccessAsync(
            await reader.Sdk.ReadLiveQueryAsync(value, token)));
        await RouteAsync(async value => (await McpCallerAssertions.SuccessAsync<LiveQueryPage>(
            await reader.Mcp.CallAsync(McpCallerTools.QueryLiveRead, value, token))).Value);

        async Task RouteAsync(Func<ReadLiveQueryRequest, Task<LiveQueryPage>> read)
        {
            LiveQueryPage? result = null;
            await CapturedPolicyClusterCurrent.BracketAsync(administrator, reader, scenario, true, async () =>
            {
                result = await read(request);
                await CursorAsync(result.Cursor, result.CutPosition);
                await SqlClusterProtocol.EqualAsync(new LiveQueryPage([.. changes], result.Cursor,
                    FeedLiveClusterProtocol.UpdatedSequence, false, result.CutPosition), result);
            }, token);
            await CapturedPolicyClusterCurrent.BracketAsync(administrator, reader, scenario, true, async () =>
            {
                var tail = await read(request with
                {
                    Cursor = result!.Cursor
                });
                await CursorAsync(tail.Cursor, tail.CutPosition);
                await SqlClusterProtocol.EqualAsync(new LiveQueryPage([], tail.Cursor,
                    FeedLiveClusterProtocol.UpdatedSequence, false, tail.CutPosition), tail);
            }, token);
        }
    }

    internal static async Task<DocumentChange> FeedDeltaAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, FeedLiveClusterScenario scenario, ReadChangeFeedRequest request,
        CommitReceipt receipt, DocumentChange? expectedOriginal, CancellationToken token)
    {
        var original = expectedOriginal;
        await RouteAsync(async value => await McpCallerAssertions.SdkSuccessAsync(
            await reader.Sdk.ReadChangesAsync(value, token)));
        await RouteAsync(async value => (await McpCallerAssertions.SuccessAsync<ChangeFeedPage>(
            await reader.Mcp.CallAsync(McpCallerTools.ChangesRead, value, token))).Value);
        return original!;

        async Task RouteAsync(Func<ReadChangeFeedRequest, Task<ChangeFeedPage>> read)
        {
            ChangeFeedPage? result = null;
            await CapturedPolicyClusterCurrent.BracketAsync(administrator, reader, scenario, true, async () =>
            {
                result = await read(request);
                await CursorAsync(result.Cursor, result.CutPosition);
                await Assert.That(result.Changes.Length).IsEqualTo(1);
                var change = result.Changes.Single();
                var expected = new DocumentChange(FeedLiveClusterProtocol.UpdatedSequence, receipt.Token,
                    change.CommittedAt, scenario.StoredReference, FeedLiveClusterProtocol.UpdatedRevision, false,
                    new(scenario.StoredReference, FeedLiveClusterProtocol.FirstRevision,
                        FeedLiveClusterProtocol.FirstProjected, true, [FeedLiveClusterProtocol.SecretField]),
                    new(scenario.StoredReference, FeedLiveClusterProtocol.UpdatedRevision,
                        FeedLiveClusterProtocol.UpdatedProjected, true, [FeedLiveClusterProtocol.SecretField]));
                await Assert.That(change.CommittedAt).IsNotEqualTo(default(DateTimeOffset));
                await SqlClusterProtocol.EqualAsync(expected, change);
                if (original is not null)
                {
                    await SqlClusterProtocol.EqualAsync(original, change);
                }
                original = change;
                await SqlClusterProtocol.EqualAsync(new ChangeFeedPage([expected], result.Cursor,
                    FeedLiveClusterProtocol.UpdatedSequence, FeedLiveClusterProtocol.UpdatedSequence,
                    FeedLiveClusterProtocol.FirstSequence, false, result.CutPosition), result);
            }, token);
            await CapturedPolicyClusterCurrent.BracketAsync(administrator, reader, scenario, true,
                async () => await EmptyFeedAsync(await read(request with
                {
                    Cursor = result!.Cursor
                }),
                    FeedLiveClusterProtocol.UpdatedSequence), token);
        }
    }

    private static async Task SnapshotAsync(LiveQuerySnapshot page)
    {
        var row = new QueryRow(FeedLiveClusterProtocol.First, FeedLiveClusterProtocol.FirstRevision,
            FeedLiveClusterProtocol.FirstProjected, true, [FeedLiveClusterProtocol.SecretField]);
        await CursorAsync(page.Cursor, page.CutPosition);
        await SqlClusterProtocol.EqualAsync(new LiveQuerySnapshot([row], page.Cursor,
            FeedLiveClusterProtocol.HiddenSequence, page.CutPosition), page);
    }

    private static async Task EmptyFeedAsync(ChangeFeedPage page, long tail)
    {
        await CursorAsync(page.Cursor, page.CutPosition);
        await SqlClusterProtocol.EqualAsync(new ChangeFeedPage([], page.Cursor, tail, tail,
            FeedLiveClusterProtocol.FirstSequence, false, page.CutPosition), page);
    }

    private static async Task CursorAsync(string cursor, long cut)
    {
        await Assert.That(string.IsNullOrEmpty(cursor)).IsFalse();
        await Assert.That(cut).IsGreaterThanOrEqualTo(0L);
    }
}

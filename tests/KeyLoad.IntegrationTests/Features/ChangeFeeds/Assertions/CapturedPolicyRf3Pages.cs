using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class CapturedPolicyRf3Pages
{
    internal static async Task<(ReadChangeFeedRequest Feed, ReadLiveQueryRequest Live)> CaptureAsync(
        RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader,
        FeedLiveRf3Scenario scenario, CancellationToken token)
    {
        LiveQuerySnapshot? snapshot = null;
        var start = new StartLiveQueryRequest(scenario.Query);
        await CapturedPolicyRf3Current.BracketAsync(administrator, reader, scenario, false, async () =>
        {
            snapshot = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.StartLiveQueryAsync(start, token));
            await SnapshotAsync(snapshot);
        }, token);
        await CapturedPolicyRf3Current.BracketAsync(administrator, reader, scenario, false, async () =>
            await SnapshotAsync((await McpCallerAssertions.SuccessAsync<LiveQuerySnapshot>(
                await reader.Mcp.CallAsync(McpCallerTools.QueryLiveStart, start, token))).Value), token);
        var feed = scenario.Feed with { Start = ChangeFeedStart.Now };
        ChangeFeedPage? caughtUp = null;
        await CapturedPolicyRf3Current.BracketAsync(administrator, reader, scenario, false, async () =>
        {
            caughtUp = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadChangesAsync(feed, token));
            await EmptyFeedAsync(caughtUp, FeedLiveRf3Protocol.HiddenSequence);
        }, token);
        await CapturedPolicyRf3Current.BracketAsync(administrator, reader, scenario, false, async () =>
            await EmptyFeedAsync((await McpCallerAssertions.SuccessAsync<ChangeFeedPage>(
                await reader.Mcp.CallAsync(McpCallerTools.ChangesRead, feed, token))).Value,
                FeedLiveRf3Protocol.HiddenSequence), token);
        return (feed with { Cursor = caughtUp!.Cursor }, new(scenario.Query, snapshot!.Cursor));
    }

    internal static async Task DeltaAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario, ReadLiveQueryRequest request,
        CommitReceipt receipt, CancellationToken token)
    {
        var row = LiveTailRf3Assertions.UpdatedRow;
        var changes = new[] { new LiveQueryChange(FeedLiveRf3Protocol.UpdatedSequence, receipt.Token,
            LiveQueryChangeKind.Upsert, scenario.Reference, FeedLiveRf3Protocol.UpdatedRevision, row) };
        await RouteAsync(async value => await McpCallerAssertions.SdkSuccessAsync(
            await reader.Sdk.ReadLiveQueryAsync(value, token)));
        await RouteAsync(async value => (await McpCallerAssertions.SuccessAsync<LiveQueryPage>(
            await reader.Mcp.CallAsync(McpCallerTools.QueryLiveRead, value, token))).Value);

        async Task RouteAsync(Func<ReadLiveQueryRequest, Task<LiveQueryPage>> read)
        {
            LiveQueryPage? result = null;
            await CapturedPolicyRf3Current.BracketAsync(administrator, reader, scenario, true, async () =>
            {
                result = await read(request);
                await CursorAsync(result.Cursor, result.CutPosition);
                await SqlRf3Protocol.EqualAsync(new LiveQueryPage([.. changes], result.Cursor,
                    FeedLiveRf3Protocol.UpdatedSequence, false, result.CutPosition), result);
            }, token);
            await CapturedPolicyRf3Current.BracketAsync(administrator, reader, scenario, true, async () =>
            {
                var tail = await read(request with { Cursor = result!.Cursor });
                await CursorAsync(tail.Cursor, tail.CutPosition);
                await SqlRf3Protocol.EqualAsync(new LiveQueryPage([], tail.Cursor,
                    FeedLiveRf3Protocol.UpdatedSequence, false, tail.CutPosition), tail);
            }, token);
        }
    }

    internal static async Task<DocumentChange> FeedDeltaAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario, ReadChangeFeedRequest request,
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
            await CapturedPolicyRf3Current.BracketAsync(administrator, reader, scenario, true, async () =>
            {
                result = await read(request);
                await CursorAsync(result.Cursor, result.CutPosition);
                await Assert.That(result.Changes.Length).IsEqualTo(1);
                var change = result.Changes.Single();
                var expected = new DocumentChange(FeedLiveRf3Protocol.UpdatedSequence, receipt.Token,
                    change.CommittedAt, scenario.Reference, FeedLiveRf3Protocol.UpdatedRevision, false,
                    new(scenario.Reference, FeedLiveRf3Protocol.FirstRevision,
                        FeedLiveRf3Protocol.FirstProjected, true, [FeedLiveRf3Protocol.SecretField]),
                    new(scenario.Reference, FeedLiveRf3Protocol.UpdatedRevision,
                        FeedLiveRf3Protocol.UpdatedProjected, true, [FeedLiveRf3Protocol.SecretField]));
                await Assert.That(change.CommittedAt).IsNotEqualTo(default(DateTimeOffset));
                await SqlRf3Protocol.EqualAsync(expected, change);
                if (original is not null)
                { await SqlRf3Protocol.EqualAsync(original, change); }
                original = change;
                await SqlRf3Protocol.EqualAsync(new ChangeFeedPage([expected], result.Cursor,
                    FeedLiveRf3Protocol.UpdatedSequence, FeedLiveRf3Protocol.UpdatedSequence,
                    FeedLiveRf3Protocol.FirstSequence, false, result.CutPosition), result);
            }, token);
            await CapturedPolicyRf3Current.BracketAsync(administrator, reader, scenario, true,
                async () => await EmptyFeedAsync(await read(request with { Cursor = result!.Cursor }),
                    FeedLiveRf3Protocol.UpdatedSequence), token);
        }
    }

    private static async Task SnapshotAsync(LiveQuerySnapshot page)
    {
        var row = new QueryRow(FeedLiveRf3Protocol.First, FeedLiveRf3Protocol.FirstRevision,
            FeedLiveRf3Protocol.FirstProjected, true, [FeedLiveRf3Protocol.SecretField]);
        await CursorAsync(page.Cursor, page.CutPosition);
        await SqlRf3Protocol.EqualAsync(new LiveQuerySnapshot([row], page.Cursor,
            FeedLiveRf3Protocol.HiddenSequence, page.CutPosition), page);
    }

    private static async Task EmptyFeedAsync(ChangeFeedPage page, long tail)
    {
        await CursorAsync(page.Cursor, page.CutPosition);
        await SqlRf3Protocol.EqualAsync(new ChangeFeedPage([], page.Cursor, tail, tail,
            FeedLiveRf3Protocol.FirstSequence, false, page.CutPosition), page);
    }

    private static async Task CursorAsync(string cursor, long cut)
    {
        await Assert.That(string.IsNullOrEmpty(cursor)).IsFalse();
        await Assert.That(cut).IsGreaterThanOrEqualTo(0L);
    }
}

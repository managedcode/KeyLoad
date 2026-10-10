using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class SnapshotInstallFeedAssertions
{
    private const string Collection = "snapshots";
    private const string Tool = FeedLiveRf3Protocol.Feed;
    private const int DocumentCount = 40;
    private const int TailCount = 1;
    private const long OriginalRevision = 1;
    private const string TailDocument = "ordered-tail";
    private const string TailJson = "{\"tail\":true}";

    internal static async Task<ChangeFeedPage> CaptureAsync(KeyLoadClient sdk,
        PartitionRef partition, CancellationToken token)
    {
        var page = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadChangesAsync(
            new(partition, Collection, Start: ChangeFeedStart.Now), token));
        await Assert.That(page.Changes.IsEmpty).IsTrue();
        await Assert.That(page.ThroughSequence).IsEqualTo(page.Tail);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
        return page;
    }

    internal static async Task RequireAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        PartitionRef partition, ChangeFeedPage original, CommitReceipt final, CommitReceipt tail, CancellationToken token)
    {
        var request = new ReadChangeFeedRequest(partition, Collection, original.Cursor);
        await RouteAsync(request, original, final, tail,
            async next => await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadChangesAsync(next, token)));
        await RouteAsync(request, original, final, tail,
            async next => (await McpCallerAssertions.SuccessAsync<ChangeFeedPage>(
                await mcp.CallAsync(Tool, next, token))).Value);
        await RouteAsync(request, original, final, tail,
            next => SqlRf3Protocol.SdkAsync<ChangeFeedPage>(sdk,
                SqlRf3Protocol.Call(request.Partition, Tool, next), token));
        await RouteAsync(request, original, final, tail,
            next => SqlRf3Protocol.McpAsync<ChangeFeedPage>(mcp,
                SqlRf3Protocol.Call(request.Partition, Tool, next), token));
    }

    private static async Task RouteAsync(ReadChangeFeedRequest request, ChangeFeedPage original,
        CommitReceipt final, CommitReceipt tail, Func<ReadChangeFeedRequest, Task<ChangeFeedPage>> read)
    {
        var page = await read(request);
        await Assert.That(page.Changes.Length).IsEqualTo(DocumentCount + TailCount);
        var position = original.CutPosition;
        for (var index = 0; index < page.Changes.Length; index++)
        {
            var actual = page.Changes[index];
            var isTail = index == DocumentCount;
            var id = isTail ? TailDocument : "doc-" + index;
            var json = isTail ? TailJson : "{\"n\":" + index + "}";
            var reference = new EntityRef(request.Partition, Collection, id);
            await Assert.That(actual.Commit.Position).IsGreaterThan(position);
            await Assert.That(actual.Commit.Position).IsLessThanOrEqualTo(tail.Token.Position);
            await Assert.That(actual.Commit.Incarnation).IsEqualTo(tail.Token.Incarnation);
            await Assert.That(actual.Commit.AtomicPartitionId).IsEqualTo(tail.Token.AtomicPartitionId);
            await Assert.That(actual.Commit.OwnershipEpoch).IsEqualTo(tail.Token.OwnershipEpoch);
            await Assert.That(actual.CommittedAt).IsNotEqualTo(default(DateTimeOffset));
            var expected = new DocumentChange(checked(original.ThroughSequence + index + TailCount),
                actual.Commit, actual.CommittedAt, reference, OriginalRevision, false, null,
                new(reference, OriginalRevision, json, false, []));
            await SqlRf3Protocol.EqualAsync(expected, actual);
            position = actual.Commit.Position;
        }
        await SqlRf3Protocol.EqualAsync(final.Token, page.Changes[DocumentCount - TailCount].Commit);
        await SqlRf3Protocol.EqualAsync(tail.Token, page.Changes[^TailCount].Commit);
        var through = checked(original.ThroughSequence + DocumentCount + TailCount);
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(tail.Token.Position);
        await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
        await SqlRf3Protocol.EqualAsync(new ChangeFeedPage(page.Changes, page.Cursor, through,
            through, original.FirstAvailable, false, page.CutPosition), page);
        var continuation = await read(request with { Cursor = page.Cursor });
        await Assert.That(string.IsNullOrEmpty(continuation.Cursor)).IsFalse();
        await Assert.That(continuation.CutPosition).IsGreaterThanOrEqualTo(tail.Token.Position);
        await SqlRf3Protocol.EqualAsync(new ChangeFeedPage([], continuation.Cursor, through,
            through, original.FirstAvailable, false, continuation.CutPosition), continuation);
    }
}

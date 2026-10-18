using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class FeedPageClusterAssertions
{
    internal static async Task RequireAsync(ChangeFeedPage actual, long sequence, long tail,
        CommitToken originalCommit, ReadChangeFeedRequest original,
        Func<ReadChangeFeedRequest, Task<ChangeFeedPage>> read)
    {
        await FeedLiveReadCoordinatesR49.RequireAsync(actual.CutPosition, originalCommit);
        await Assert.That(string.IsNullOrEmpty(actual.Cursor)).IsFalse();
        await SqlClusterProtocol.EqualAsync(new ChangeFeedPage(actual.Changes, actual.Cursor, sequence, tail,
            FeedLiveClusterProtocol.FirstSequence, sequence < tail, actual.CutPosition), actual);
        var continuation = await read(original with
        {
            Cursor = actual.Cursor
        });
        // Exactly one fixture position can be examined: hidden sequence2 or an empty caught-up tail.
        var through = sequence == FeedLiveClusterProtocol.FirstSequence ? FeedLiveClusterProtocol.HiddenSequence : sequence;
        await FeedLiveReadCoordinatesR49.RequireAsync(continuation.CutPosition, originalCommit);
        await Assert.That(string.IsNullOrEmpty(continuation.Cursor)).IsFalse();
        await SqlClusterProtocol.EqualAsync(new ChangeFeedPage([], continuation.Cursor, through, tail,
            FeedLiveClusterProtocol.FirstSequence, through < tail, continuation.CutPosition), continuation);
    }
}

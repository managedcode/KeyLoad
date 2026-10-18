namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class FeedLiveReadCoordinatesR49
{
    private const long MinimumStorageCutExclusive = 0;
    private const long MinimumCanonicalCommitExclusive = 0;

    internal static async Task RequireAsync(long storageCut, CommitToken originalCommit)
    {
        await Assert.That(storageCut).IsGreaterThan(MinimumStorageCutExclusive);
        await Assert.That(originalCommit.Position).IsGreaterThan(MinimumCanonicalCommitExclusive);
    }
}

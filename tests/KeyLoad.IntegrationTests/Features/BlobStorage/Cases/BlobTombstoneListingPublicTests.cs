namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "blob-tombstone-listing-public")]
[NotInParallel]
internal sealed class BlobTombstoneListingPublicTests(ClusterFixture fixture)
{
    [Test]
    public Task RetiredAndUnpublishedHeadsAdvanceEmptyCursorWhileCurrentRevisionAndOriginalResultsSurviveTwoRf3ColdCycles()
        => BlobTombstoneListingPublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "blob-supersession-public")]
[NotInParallel]
internal sealed class BlobSupersessionPublicTests(ClusterFixture fixture)
{
    [Test]
    public Task SameBlobSupersessionPreservesFullReceiptsAndCurrentRangesAcrossTwoClusterColdCycles()
        => BlobSupersessionPublicTrial.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
